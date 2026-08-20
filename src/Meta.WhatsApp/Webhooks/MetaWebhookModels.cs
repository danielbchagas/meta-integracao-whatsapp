using System.Text.Json;
using Meta.WhatsApp.Sessions;

namespace Meta.WhatsApp.Webhooks;

/// <summary>Notifications extracted from a WhatsApp Business Account webhook payload.</summary>
public sealed record MetaWebhookNotification(
    IReadOnlyList<InboundWebhookMessage> InboundMessages,
    IReadOnlyList<MessageStatusWebhookUpdate> StatusUpdates);

/// <summary>A customer message received through the WhatsApp webhook.</summary>
public sealed record InboundWebhookMessage(
    string ChannelId,
    string Recipient,
    string MessageId,
    DateTimeOffset ReceivedAtUtc,
    string Type,
    string? ContextMessageId,
    JsonElement Payload)
{
    public InboundMessage ToInboundMessage() =>
        new(Recipient, MessageId, ReceivedAtUtc, ContextMessageId);
}

/// <summary>A delivery status received through the WhatsApp webhook.</summary>
public sealed record MessageStatusWebhookUpdate(
    string ChannelId,
    string Recipient,
    string MessageId,
    string Status,
    DateTimeOffset OccurredAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    JsonElement Payload)
{
    public bool TryCreateReengagementStatusUpdate(out ReengagementStatusUpdate? update)
    {
        if (!Enum.TryParse<ReengagementMessageStatus>(Status, ignoreCase: true, out var parsedStatus) ||
            parsedStatus is not (
                ReengagementMessageStatus.Sent or
                ReengagementMessageStatus.Delivered or
                ReengagementMessageStatus.Read or
                ReengagementMessageStatus.Failed))
        {
            update = null;
            return false;
        }

        update = new ReengagementStatusUpdate(
            Recipient,
            MessageId,
            parsedStatus,
            OccurredAtUtc,
            ErrorCode,
            ErrorMessage);
        return true;
    }
}

/// <summary>Result of processing all notifications for a single configured WhatsApp channel.</summary>
public sealed record MetaWebhookProcessingResult(
    IReadOnlyList<InboundRegistrationResult> InboundRegistrations,
    IReadOnlyList<ConversationSession?> StatusUpdates,
    int IgnoredNotifications)
{
    public bool WasAnySessionReactivated =>
        InboundRegistrations.Any(registration => registration.WasReactivated);
}
