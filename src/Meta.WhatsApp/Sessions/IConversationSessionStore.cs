namespace Meta.WhatsApp.Sessions;

/// <summary>Persistence abstraction for service sessions. Implement this interface for distributed applications.</summary>
public interface IConversationSessionStore
{
    ValueTask<ConversationSession?> GetAsync(
        string channelId,
        string recipient,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically stores an inbound message without replacing a newer inbound message.</summary>
    ValueTask<ConversationSession> RegisterInboundAsync(
        ConversationSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically reserves a reengagement attempt and enforces idempotency and cooldown.</summary>
    ValueTask<ReengagementReservation> TryReserveReengagementAsync(
        string channelId,
        string recipient,
        ReengagementAttempt attempt,
        TimeSpan cooldown,
        int maxHistory,
        CancellationToken cancellationToken = default);

    ValueTask<ConversationSession?> UpdateReengagementAsync(
        string channelId,
        string recipient,
        string idempotencyKey,
        ReengagementMessageStatus status,
        DateTimeOffset statusUpdatedAtUtc,
        string? messageId = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    ValueTask<ConversationSession?> UpdateReengagementStatusAsync(
        string channelId,
        string recipient,
        string messageId,
        ReengagementMessageStatus status,
        DateTimeOffset statusUpdatedAtUtc,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);

    ValueTask<ConversationSession?> MarkExpiredAsync(
        string channelId,
        string recipient,
        DateTimeOffset expiredAtUtc,
        bool force,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(
        string channelId,
        string recipient,
        CancellationToken cancellationToken = default);
}
