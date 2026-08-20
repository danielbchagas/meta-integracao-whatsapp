namespace Meta.WhatsApp.Sessions;

/// <summary>
/// Local representation of the customer-service window opened by an inbound WhatsApp message.
/// Meta does not expose a reusable server-side session identifier.
/// </summary>
public sealed record ConversationSession(
    string ChannelId,
    string Recipient,
    string LastInboundMessageId,
    DateTimeOffset LastInboundMessageAtUtc,
    DateTimeOffset ExpiresAtUtc,
    ConversationSessionState State,
    IReadOnlyList<ReengagementAttempt> ReengagementAttempts)
{
    public bool IsOpen(DateTimeOffset nowUtc) =>
        State == ConversationSessionState.Open && nowUtc < ExpiresAtUtc;

    public ReengagementAttempt? LastReengagementAttempt =>
        ReengagementAttempts.LastOrDefault();
}

public enum ConversationSessionState
{
    Open,
    Expired,
    ReengagementPending
}
