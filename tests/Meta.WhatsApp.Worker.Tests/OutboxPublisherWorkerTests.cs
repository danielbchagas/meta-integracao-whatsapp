using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;
using Meta.WhatsApp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meta.WhatsApp.Worker.Tests;

public sealed class OutboxPublisherWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PublishesClaimedMessageAndMarksItAsPublished()
    {
        var repository = new RecordingOutboxRepository(CreateMessage());
        var publisher = new RecordingPublisher();
        await using var services = CreateServices(repository);
        using var worker = CreateWorker(services, publisher);

        await worker.StartAsync(CancellationToken.None);
        await repository.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Single(publisher.Published);
        Assert.Equal(repository.Message.Id, publisher.Published[0]);
        Assert.True(repository.WasMarkedPublished);
        Assert.False(repository.WasMarkedFailed);
    }

    [Fact]
    public async Task PublishFailureIsRecordedWithExponentialRetry()
    {
        var repository = new RecordingOutboxRepository(CreateMessage());
        var publisher = new RecordingPublisher(new InvalidOperationException("service bus unavailable"));
        await using var services = CreateServices(repository);
        using var worker = CreateWorker(services, publisher);

        await worker.StartAsync(CancellationToken.None);
        await repository.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(repository.WasMarkedFailed);
        Assert.False(repository.WasMarkedPublished);
        Assert.Equal("service bus unavailable", repository.LastError);
        Assert.NotNull(repository.NextAttemptAt);
    }

    private static ServiceProvider CreateServices(RecordingOutboxRepository repository)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxRepository>(repository);
        return services.BuildServiceProvider();
    }

    private static OutboxPublisherWorker CreateWorker(
        ServiceProvider services,
        IIntegrationEventPublisher publisher) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        publisher,
        Options.Create(new WorkerOptions
        {
            OutboxBatchSize = 10,
            PollIntervalSeconds = 60,
            ClaimTimeoutSeconds = 120,
            MaxAttempts = 10
        }),
        TimeProvider.System,
        NullLogger<OutboxPublisherWorker>.Instance);

    private static OutboxMessage CreateMessage() => new(
        Guid.NewGuid(),
        "Message",
        "message-1",
        "message.requested",
        "{}",
        Now,
        Guid.NewGuid());

    private sealed class RecordingOutboxRepository(OutboxMessage message) : IOutboxRepository
    {
        private bool _claimed;

        public OutboxMessage Message { get; } = message;

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasMarkedPublished { get; private set; }

        public bool WasMarkedFailed { get; private set; }

        public string? LastError { get; private set; }

        public DateTimeOffset? NextAttemptAt { get; private set; }

        public void Add(OutboxMessage outboxMessage) => throw new NotSupportedException();

        public Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
            int batchSize,
            string workerId,
            DateTimeOffset now,
            TimeSpan claimTimeout,
            CancellationToken cancellationToken)
        {
            if (_claimed)
            {
                return Task.FromResult<IReadOnlyList<OutboxMessage>>([]);
            }

            _claimed = true;
            Message.Claim(workerId, now);
            return Task.FromResult<IReadOnlyList<OutboxMessage>>([Message]);
        }

        public Task MarkPublishedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
        {
            WasMarkedPublished = true;
            Message.MarkPublished(now);
            Completed.TrySetResult();
            return Task.CompletedTask;
        }

        public Task RegisterFailureAsync(
            Guid id,
            string errorMessage,
            DateTimeOffset nextAttemptAt,
            CancellationToken cancellationToken)
        {
            WasMarkedFailed = true;
            LastError = errorMessage;
            NextAttemptAt = nextAttemptAt;
            Message.RegisterFailure(errorMessage, nextAttemptAt);
            Completed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher(Exception? failure = null) : IIntegrationEventPublisher
    {
        public List<Guid> Published { get; } = [];

        public Task PublishAsync(
            Guid messageId,
            string eventType,
            string payload,
            Guid correlationId,
            CancellationToken cancellationToken)
        {
            Published.Add(messageId);
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
}
