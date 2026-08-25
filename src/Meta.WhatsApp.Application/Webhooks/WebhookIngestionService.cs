using System.Security.Cryptography;
using System.Text;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Webhooks;

public sealed record IncomingWebhookEvent(
    string Kind,
    string ChannelId,
    string MetaObjectId,
    string? Recipient,
    string? Status,
    DateTimeOffset OccurredAt,
    string PayloadJson,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record WebhookIngestionResult(int Accepted, int Duplicates);

public sealed class WebhookIngestionService
{
    private readonly IWebhookEventStore _store;
    private readonly TimeProvider _timeProvider;

    public WebhookIngestionService(IWebhookEventStore store, TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<WebhookIngestionResult> IngestAsync(
        IReadOnlyCollection<IncomingWebhookEvent> incomingEvents,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incomingEvents);
        var accepted = 0;
        var duplicates = 0;
        foreach (var incoming in incomingEvents)
        {
            var inboxId = CreateDeterministicId(incoming);
            var receivedAt = _timeProvider.GetUtcNow();
            var integrationEvent = new WebhookReceivedEvent(
                inboxId,
                incoming.Kind,
                incoming.ChannelId,
                incoming.MetaObjectId,
                incoming.Recipient,
                incoming.Status,
                incoming.OccurredAt,
                incoming.PayloadJson,
                incoming.ErrorCode,
                incoming.ErrorMessage);
            var inbox = new InboxMessage(
                inboxId,
                IntegrationEventTypes.WebhookReceived,
                ApplicationJson.Serialize(integrationEvent),
                receivedAt);
            var outbox = new OutboxMessage(
                Guid.NewGuid(),
                nameof(InboxMessage),
                inboxId.ToString("D"),
                IntegrationEventTypes.WebhookReceived,
                ApplicationJson.Serialize(integrationEvent),
                receivedAt,
                correlationId);

            if (await _store.TryEnqueueAsync(inbox, outbox, cancellationToken).ConfigureAwait(false))
            {
                accepted++;
            }
            else
            {
                duplicates++;
            }
        }

        return new WebhookIngestionResult(accepted, duplicates);
    }

    private static Guid CreateDeterministicId(IncomingWebhookEvent incoming)
    {
        var identity = string.Join(
            '\n',
            incoming.Kind,
            incoming.ChannelId,
            incoming.MetaObjectId,
            incoming.Status ?? string.Empty,
            incoming.OccurredAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(identity), hash);
        return new Guid(hash[..16]);
    }
}
