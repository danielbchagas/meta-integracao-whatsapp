using Meta.WhatsApp.Messages;

namespace Meta.WhatsApp.Sessions;

/// <summary>Request for contacting a recipient again after the customer-service window has closed.</summary>
public sealed record ReengagementRequest(
    string Recipient,
    string TemplateName,
    string LanguageCode,
    string IdempotencyKey,
    IReadOnlyList<TemplateMessageComponent>? Components = null);

public sealed record ReengagementAttempt(
    string IdempotencyKey,
    string TemplateName,
    string LanguageCode,
    DateTimeOffset RequestedAtUtc,
    ReengagementMessageStatus Status,
    DateTimeOffset StatusUpdatedAtUtc,
    string? MessageId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public enum ReengagementMessageStatus
{
    Submitting,
    Accepted,
    Sent,
    Delivered,
    Read,
    Failed,
    Unknown
}

public enum ReengagementAction
{
    Submitted,
    AlreadySubmitted,
    InProgress,
    PreviouslyFailed
}

public sealed record ReengagementResult(
    ReengagementAction Action,
    string ChannelId,
    string Recipient,
    string IdempotencyKey,
    ReengagementMessageStatus Status,
    string? MessageId);

/// <summary>A delivery-status webhook update for a reengagement template message.</summary>
public sealed record ReengagementStatusUpdate(
    string Recipient,
    string MessageId,
    ReengagementMessageStatus Status,
    DateTimeOffset? OccurredAtUtc = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public enum ReengagementReservationOutcome
{
    Reserved,
    Duplicate,
    CooldownActive,
    SessionNotFound,
    SessionOpen
}

public sealed record ReengagementReservation(
    ReengagementReservationOutcome Outcome,
    ConversationSession? Session,
    ReengagementAttempt? Attempt,
    DateTimeOffset? RetryAtUtc = null);
