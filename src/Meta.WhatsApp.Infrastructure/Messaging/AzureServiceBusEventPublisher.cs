using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Infrastructure.Messaging;

public sealed class AzureServiceBusEventPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusOptions _options;
    private readonly TokenCredential _credential;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private ServiceBusClient? _client;
    private ServiceBusSender? _sender;

    public AzureServiceBusEventPublisher(IOptions<ServiceBusOptions> options, TokenCredential credential)
    {
        _options = options.Value;
        _credential = credential;
    }

    public async Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var sender = await GetSenderAsync(cancellationToken).ConfigureAwait(false);
        var message = new ServiceBusMessage(BinaryData.FromString(payload))
        {
            MessageId = messageId.ToString("D"),
            Subject = Required(eventType, nameof(eventType)),
            CorrelationId = correlationId.ToString("D"),
            ContentType = "application/json"
        };
        message.ApplicationProperties["schema"] = eventType;
        await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender is not null)
        {
            await _sender.DisposeAsync().ConfigureAwait(false);
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        _initializationLock.Dispose();
    }

    private async Task<ServiceBusSender> GetSenderAsync(CancellationToken cancellationToken)
    {
        if (_sender is not null)
        {
            return _sender;
        }

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sender is not null)
            {
                return _sender;
            }

            var serviceNamespace = Required(
                _options.FullyQualifiedNamespace,
                nameof(_options.FullyQualifiedNamespace));
            _client = new ServiceBusClient(serviceNamespace, _credential);
            _sender = _client.CreateSender(Required(_options.TopicName, nameof(_options.TopicName)));
            return _sender;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static string Required(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{parameterName} must be configured.")
            : value.Trim();
}
