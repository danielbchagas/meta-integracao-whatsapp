using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Webhooks;

public sealed record WebhookProcessingResult(Guid InboxMessageId, bool Processed, bool MessageMatched);

public sealed class WebhookEventProcessor
{
    private readonly IInboxRepository _inbox;
    private readonly IMessageRepository _messages;
    private readonly IOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public WebhookEventProcessor(
        IInboxRepository inbox,
        IMessageRepository messages,
        IOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _inbox = inbox;
        _messages = messages;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<WebhookProcessingResult> ProcessAsync(
        WebhookReceivedEvent webhookEvent,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);
        var inboxMessage = await _inbox.GetAsync(webhookEvent.InboxMessageId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Inbox message '{webhookEvent.InboxMessageId}' was not found.");
        if (inboxMessage.ProcessedAt is not null)
        {
            return new WebhookProcessingResult(inboxMessage.MessageId, false, false);
        }

        WhatsAppMessage? matchedMessage = null;
        if (string.Equals(webhookEvent.Kind, "status", StringComparison.OrdinalIgnoreCase))
        {
            matchedMessage = await _messages
                .GetByMetaMessageIdAsync(webhookEvent.MetaObjectId, cancellationToken)
                .ConfigureAwait(false);
            if (matchedMessage is not null)
            {
                ApplyStatus(matchedMessage, webhookEvent);
            }
        }

        var now = _timeProvider.GetUtcNow();
        await _unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                inboxMessage.MarkProcessed(now);
                var eventType = string.Equals(webhookEvent.Kind, "message", StringComparison.OrdinalIgnoreCase)
                    ? IntegrationEventTypes.InboundMessageReceived
                    : IntegrationEventTypes.MessageStatusChanged;
                _outbox.Add(new OutboxMessage(
                    Guid.NewGuid(),
                    matchedMessage is null ? nameof(InboxMessage) : nameof(WhatsAppMessage),
                    matchedMessage?.Id.ToString("D") ?? inboxMessage.MessageId.ToString("D"),
                    eventType,
                    matchedMessage is null
                        ? ApplicationJson.Serialize(webhookEvent)
                        : ApplicationJson.Serialize(new MessageStatusChangedEvent(
                            matchedMessage.Id,
                            matchedMessage.Status,
                            matchedMessage.MetaMessageId,
                            webhookEvent.OccurredAt,
                            webhookEvent.ErrorMessage)),
                    now,
                    correlationId,
                    inboxMessage.MessageId));
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);
        return new WebhookProcessingResult(inboxMessage.MessageId, true, matchedMessage is not null);
    }

    private static void ApplyStatus(WhatsAppMessage message, WebhookReceivedEvent webhookEvent)
    {
        switch (webhookEvent.Status?.ToLowerInvariant())
        {
            case "sent":
                if (message.Status == WhatsAppMessageStatus.Sending)
                {
                    message.MarkSent(webhookEvent.MetaObjectId, webhookEvent.OccurredAt);
                }

                break;
            case "delivered":
                message.MarkDelivered(webhookEvent.OccurredAt);
                break;
            case "read":
                message.MarkRead(webhookEvent.OccurredAt);
                break;
            case "failed" when message.Status is WhatsAppMessageStatus.Sending or WhatsAppMessageStatus.Sent:
                message.TransitionTo(
                    WhatsAppMessageStatus.FailedPermanent,
                    webhookEvent.OccurredAt,
                    webhookEvent.ErrorMessage ?? webhookEvent.ErrorCode);
                break;
        }
    }
}
