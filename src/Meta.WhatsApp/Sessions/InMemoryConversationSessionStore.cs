namespace Meta.WhatsApp.Sessions;

/// <summary>Thread-safe, process-local session store suitable for a single application instance.</summary>
public sealed class InMemoryConversationSessionStore : IConversationSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string ChannelId, string Recipient), ConversationSession> _sessions = [];

    public ValueTask<ConversationSession?> GetAsync(
        string channelId,
        string recipient,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _sessions.TryGetValue((channelId, recipient), out var session);
            return ValueTask.FromResult(session);
        }
    }

    public ValueTask<ConversationSession> RegisterInboundAsync(
        ConversationSession session,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (session.ChannelId, session.Recipient);
            if (_sessions.TryGetValue(key, out var existing) &&
                existing.LastInboundMessageAtUtc > session.LastInboundMessageAtUtc)
            {
                return ValueTask.FromResult(existing);
            }

            var stored = session with
            {
                State = ConversationSessionState.Open,
                ReengagementAttempts = existing?.ReengagementAttempts ?? session.ReengagementAttempts
            };
            _sessions[key] = stored;
            return ValueTask.FromResult(stored);
        }
    }

    public ValueTask<ReengagementReservation> TryReserveReengagementAsync(
        string channelId,
        string recipient,
        ReengagementAttempt attempt,
        TimeSpan cooldown,
        int maxHistory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (channelId, recipient);
            if (!_sessions.TryGetValue(key, out var session))
            {
                return ValueTask.FromResult(new ReengagementReservation(
                    ReengagementReservationOutcome.SessionNotFound,
                    Session: null,
                    Attempt: null));
            }

            session = RefreshExpiration(session, attempt.RequestedAtUtc);
            _sessions[key] = session;

            var duplicate = session.ReengagementAttempts.FirstOrDefault(item =>
                string.Equals(item.IdempotencyKey, attempt.IdempotencyKey, StringComparison.Ordinal));
            if (duplicate is not null)
            {
                return ValueTask.FromResult(new ReengagementReservation(
                    ReengagementReservationOutcome.Duplicate,
                    session,
                    duplicate));
            }

            if (session.IsOpen(attempt.RequestedAtUtc))
            {
                return ValueTask.FromResult(new ReengagementReservation(
                    ReengagementReservationOutcome.SessionOpen,
                    session,
                    Attempt: null));
            }

            var previous = session.LastReengagementAttempt;
            var retryAtUtc = previous?.RequestedAtUtc.Add(cooldown);
            if (retryAtUtc > attempt.RequestedAtUtc)
            {
                return ValueTask.FromResult(new ReengagementReservation(
                    ReengagementReservationOutcome.CooldownActive,
                    session,
                    previous,
                    retryAtUtc));
            }

            var history = session.ReengagementAttempts
                .Append(attempt)
                .TakeLast(maxHistory)
                .ToArray();
            var reserved = session with
            {
                State = ConversationSessionState.ReengagementPending,
                ReengagementAttempts = history
            };
            _sessions[key] = reserved;

            return ValueTask.FromResult(new ReengagementReservation(
                ReengagementReservationOutcome.Reserved,
                reserved,
                attempt));
        }
    }

    public ValueTask<ConversationSession?> UpdateReengagementAsync(
        string channelId,
        string recipient,
        string idempotencyKey,
        ReengagementMessageStatus status,
        DateTimeOffset statusUpdatedAtUtc,
        string? messageId = null,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(UpdateAttempt(
                channelId,
                recipient,
                attempt => string.Equals(attempt.IdempotencyKey, idempotencyKey, StringComparison.Ordinal),
                status,
                statusUpdatedAtUtc,
                messageId,
                errorCode,
                errorMessage));
        }
    }

    public ValueTask<ConversationSession?> UpdateReengagementStatusAsync(
        string channelId,
        string recipient,
        string messageId,
        ReengagementMessageStatus status,
        DateTimeOffset statusUpdatedAtUtc,
        string? errorCode = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(UpdateAttempt(
                channelId,
                recipient,
                attempt => string.Equals(attempt.MessageId, messageId, StringComparison.Ordinal),
                status,
                statusUpdatedAtUtc,
                messageId,
                errorCode,
                errorMessage));
        }
    }

    public ValueTask<ConversationSession?> MarkExpiredAsync(
        string channelId,
        string recipient,
        DateTimeOffset expiredAtUtc,
        bool force,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = (channelId, recipient);
            if (!_sessions.TryGetValue(key, out var session))
            {
                return ValueTask.FromResult<ConversationSession?>(null);
            }

            if (!force && (session.State != ConversationSessionState.Open || session.ExpiresAtUtc > expiredAtUtc))
            {
                return ValueTask.FromResult<ConversationSession?>(session);
            }

            var expired = session with
            {
                ExpiresAtUtc = force && session.ExpiresAtUtc > expiredAtUtc
                    ? expiredAtUtc
                    : session.ExpiresAtUtc,
                State = ConversationSessionState.Expired
            };
            _sessions[key] = expired;
            return ValueTask.FromResult<ConversationSession?>(expired);
        }
    }

    public ValueTask RemoveAsync(
        string channelId,
        string recipient,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _sessions.Remove((channelId, recipient));
            return ValueTask.CompletedTask;
        }
    }

    private ConversationSession? UpdateAttempt(
        string channelId,
        string recipient,
        Func<ReengagementAttempt, bool> predicate,
        ReengagementMessageStatus status,
        DateTimeOffset statusUpdatedAtUtc,
        string? messageId,
        string? errorCode,
        string? errorMessage)
    {
        var key = (channelId, recipient);
        if (!_sessions.TryGetValue(key, out var session))
        {
            return null;
        }

        var attempts = session.ReengagementAttempts.ToArray();
        var index = Array.FindIndex(attempts, attempt => predicate(attempt));
        if (index < 0)
        {
            return session;
        }

        var current = attempts[index];
        if (IsStatusRegression(current.Status, status))
        {
            return session;
        }

        attempts[index] = current with
        {
            Status = status,
            StatusUpdatedAtUtc = statusUpdatedAtUtc,
            MessageId = messageId ?? current.MessageId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

        var state = session.State == ConversationSessionState.Open
            ? ConversationSessionState.Open
            : attempts[^1].Status == ReengagementMessageStatus.Failed
                ? ConversationSessionState.Expired
                : ConversationSessionState.ReengagementPending;
        var updated = session with { State = state, ReengagementAttempts = attempts };
        _sessions[key] = updated;
        return updated;
    }

    private static ConversationSession RefreshExpiration(
        ConversationSession session,
        DateTimeOffset nowUtc) =>
        session.State == ConversationSessionState.Open && session.ExpiresAtUtc <= nowUtc
            ? session with { State = ConversationSessionState.Expired }
            : session;

    private static bool IsStatusRegression(
        ReengagementMessageStatus current,
        ReengagementMessageStatus next)
    {
        if (next is ReengagementMessageStatus.Failed or ReengagementMessageStatus.Unknown)
        {
            return false;
        }

        return Rank(next) < Rank(current);
    }

    private static int Rank(ReengagementMessageStatus status) => status switch
    {
        ReengagementMessageStatus.Submitting => 0,
        ReengagementMessageStatus.Accepted => 1,
        ReengagementMessageStatus.Sent => 2,
        ReengagementMessageStatus.Delivered => 3,
        ReengagementMessageStatus.Read => 4,
        ReengagementMessageStatus.Unknown => 5,
        ReengagementMessageStatus.Failed => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
