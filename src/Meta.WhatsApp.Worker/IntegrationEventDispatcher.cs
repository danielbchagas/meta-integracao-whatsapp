using System.Text.Json;
using Meta.WhatsApp.Application;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Webhooks;

namespace Meta.WhatsApp.Worker;

public sealed class IntegrationEventDispatcher(
    MessageProcessor messageProcessor,
    TemplateMutationProcessor templateProcessor,
    WebhookEventProcessor webhookProcessor)
{
    public async Task DispatchAsync(
        string eventType,
        string payload,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        switch (eventType)
        {
            case IntegrationEventTypes.MessageRequested:
                await messageProcessor.ProcessAsync(
                        Deserialize<MessageRequestedEvent>(payload),
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case IntegrationEventTypes.TemplateCreateRequested:
            case IntegrationEventTypes.TemplateUpdateRequested:
            case IntegrationEventTypes.TemplateDeleteRequested:
                await templateProcessor.ProcessAsync(
                        Deserialize<TemplateMutationRequestedEvent>(payload),
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case IntegrationEventTypes.WebhookReceived:
                await webhookProcessor.ProcessAsync(
                        Deserialize<WebhookReceivedEvent>(payload),
                        correlationId,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case IntegrationEventTypes.WabaSynchronized:
            case IntegrationEventTypes.MessageStatusChanged:
            case IntegrationEventTypes.InboundMessageReceived:
                break;
            default:
                throw new InvalidOperationException($"Unsupported integration event type '{eventType}'.");
        }
    }

    private static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, ApplicationJson.SerializerOptions)
        ?? throw new JsonException($"Integration event '{typeof(T).Name}' has an empty payload.");
}
