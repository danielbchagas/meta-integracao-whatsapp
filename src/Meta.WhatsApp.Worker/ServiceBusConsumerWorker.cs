using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;
using Meta.WhatsApp.Infrastructure;

namespace Meta.WhatsApp.Worker;

public sealed class ServiceBusConsumerWorker : IHostedService, IAsyncDisposable
{
    private static readonly Action<ILogger, string, int, Exception?> LogProcessingFailure =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(2101, nameof(ServiceBusConsumerWorker)),
            "Service Bus message {MessageId} failed on delivery {DeliveryCount}");

    private static readonly Action<ILogger, string, Exception?> LogProcessorFailure =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2102, nameof(ServiceBusConsumerWorker)),
            "Service Bus processor error from {ErrorSource}");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServiceBusConsumerWorker> _logger;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusProcessor _processor;

    public ServiceBusConsumerWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ServiceBusOptions> options,
        TokenCredential credential,
        TimeProvider timeProvider,
        ILogger<ServiceBusConsumerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        var serviceNamespace = string.IsNullOrWhiteSpace(_options.FullyQualifiedNamespace)
            ? "not-configured.servicebus.windows.net"
            : _options.FullyQualifiedNamespace;
        _client = new ServiceBusClient(serviceNamespace, credential);
        _processor = _client.CreateProcessor(
            _options.TopicName,
            _options.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = _options.MaxConcurrentCalls,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
            });
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.FullyQualifiedNamespace))
        {
            return Task.CompletedTask;
        }

        return _processor.StartProcessingAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        _processor.IsProcessing
            ? _processor.StopProcessingAsync(cancellationToken)
            : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = Guid.TryParse(args.Message.MessageId, out var parsedId)
            ? parsedId
            : CreateMessageId(args.Message.MessageId);
        var payload = args.Message.Body.ToString();
        using var scope = _scopeFactory.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxRepository>();
        var accepted = await inbox.TryAddAsync(
                new InboxMessage(messageId, args.Message.Subject, payload, _timeProvider.GetUtcNow()),
                args.CancellationToken)
            .ConfigureAwait(false);
        if (!accepted)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();
            var correlationId = Guid.TryParse(args.Message.CorrelationId, out var parsedCorrelation)
                ? parsedCorrelation
                : Guid.NewGuid();
            await dispatcher.DispatchAsync(
                    args.Message.Subject,
                    payload,
                    correlationId,
                    args.CancellationToken)
                .ConfigureAwait(false);
            await inbox.MarkProcessedAsync(messageId, _timeProvider.GetUtcNow(), args.CancellationToken)
                .ConfigureAwait(false);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProcessingFailure(_logger, args.Message.MessageId, args.Message.DeliveryCount, exception);
            await inbox.RegisterFailureAsync(messageId, exception.Message, CancellationToken.None).ConfigureAwait(false);
            if (args.Message.DeliveryCount >= _options.MaxDeliveryCount)
            {
                await args.DeadLetterMessageAsync(
                        args.Message,
                        "max-delivery-count",
                        exception.Message,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message, cancellationToken: CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorFailure(_logger, args.ErrorSource.ToString(), args.Exception);
        return Task.CompletedTask;
    }

    private static Guid CreateMessageId(string value)
    {
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value), hash);
        return new Guid(hash[..16]);
    }
}
