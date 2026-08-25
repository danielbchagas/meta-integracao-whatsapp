namespace Meta.WhatsApp.Sessions;

/// <summary>Outcome of atomically registering an inbound customer message.</summary>
public sealed record InboundRegistrationResult(
    ConversationSession Session,
    ConversationSessionState? PreviousState,
    InboundRegistrationOutcome Outcome,
    string MessageId,
    string? ContextMessageId)
{
    /// <summary>
    /// Indicates that a closed or reengagement-pending session became open because of this message.
    /// </summary>
    public bool WasReactivated => Outcome == InboundRegistrationOutcome.Reactivated;

    /// <summary>The reengagement attempt explicitly referenced by the inbound message, when available.</summary>
    public ReengagementAttempt? MatchedReengagementAttempt =>
        string.IsNullOrWhiteSpace(ContextMessageId)
            ? null
            : Session.ReengagementAttempts.LastOrDefault(attempt =>
                string.Equals(
                    attempt.MessageId,
                    ContextMessageId,
                    StringComparison.Ordinal));

    public bool IsReplyToReengagement => MatchedReengagementAttempt is not null;
}

public enum InboundRegistrationOutcome
{
    Opened,
    Renewed,
    Reactivated,
    Duplicate,
    IgnoredOutOfOrder
}
