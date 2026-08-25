using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Infrastructure;

namespace Meta.WhatsApp.Worker;

public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IIntegrationEventPublisher publisher,
    IOptions<WorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Guid, int, Exception?> LogPublishFailure =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Warning,
            new EventId(2001, nameof(OutboxPublisherWorker)),
            "Outbox message {MessageId} failed on attempt {Attempt}");

    private readonly WorkerOptions _options = options.Value;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.PollIntervalSeconds),
            timeProvider);
        do
        {
            await PublishBatchAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PublishBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var messages = await outbox.ClaimPendingAsync(
                _options.OutboxBatchSize,
                _workerId,
                timeProvider.GetUtcNow(),
                TimeSpan.FromSeconds(_options.ClaimTimeoutSeconds),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(
                        message.Id,
                        message.EventType,
                        message.Payload,
                        message.CorrelationId,
                        cancellationToken)
                    .ConfigureAwait(false);
                await outbox.MarkPublishedAsync(message.Id, timeProvider.GetUtcNow(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var attempt = message.AttemptCount + 1;
                LogPublishFailure(logger, message.Id, attempt, exception);
                var delay = TimeSpan.FromSeconds(Math.Min(3600, 10 * Math.Pow(2, Math.Min(attempt, 8))));
                await outbox.RegisterFailureAsync(
                        message.Id,
                        exception.Message,
                        timeProvider.GetUtcNow().Add(delay),
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }
}
