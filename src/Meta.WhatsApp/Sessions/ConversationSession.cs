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
    /// <summary>The outbound message explicitly referenced by the latest inbound reply.</summary>
    public string? LastInboundContextMessageId { get; init; }

    /// <summary>Recent inbound message identifiers retained for webhook idempotency.</summary>
    public IReadOnlyList<string> ProcessedInboundMessageIds { get; init; } = [];

    public bool IsOpen(DateTimeOffset nowUtc) =>
        State == ConversationSessionState.Open && nowUtc < ExpiresAtUtc;

    public ReengagementAttempt? LastReengagementAttempt =>
        ReengagementAttempts.Count == 0 ? null : ReengagementAttempts[^1];
}

public enum ConversationSessionState
{
    Open,
    Expired,
    ReengagementPending
}
