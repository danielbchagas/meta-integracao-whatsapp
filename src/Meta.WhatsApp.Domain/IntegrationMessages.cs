namespace Meta.WhatsApp.Domain;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(
        Guid id,
        string aggregateType,
        string aggregateId,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        Guid correlationId,
        Guid? causationId = null)
    {
        Id = id;
        AggregateType = Required(aggregateType, nameof(aggregateType));
        AggregateId = Required(aggregateId, nameof(aggregateId));
        EventType = Required(eventType, nameof(eventType));
        Payload = Required(payload, nameof(payload));
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        CausationId = causationId;
    }

    public Guid Id { get; private set; }

    public string AggregateType { get; private set; } = null!;

    public string AggregateId { get; private set; } = null!;

    public string EventType { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public Guid CorrelationId { get; private set; }

    public Guid? CausationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? ClaimedAt { get; private set; }

    public string? ClaimedBy { get; private set; }

    public string? LastError { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Claim(string workerId, DateTimeOffset now)
    {
        ClaimedBy = Required(workerId, nameof(workerId));
        ClaimedAt = now;
    }

    public void MarkPublished(DateTimeOffset now)
    {
        PublishedAt = now;
        ClaimedAt = null;
        ClaimedBy = null;
        LastError = null;
    }

    public void RegisterFailure(string error, DateTimeOffset nextAttemptAt)
    {
        AttemptCount++;
        LastError = Required(error, nameof(error));
        NextAttemptAt = nextAttemptAt;
        ClaimedAt = null;
        ClaimedBy = null;
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
}

public sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    public InboxMessage(Guid messageId, string messageType, string? payload, DateTimeOffset receivedAt)
    {
        MessageId = messageId;
        MessageType = string.IsNullOrWhiteSpace(messageType)
            ? throw new ArgumentException("A value is required.", nameof(messageType))
            : messageType.Trim();
        Payload = payload;
        ReceivedAt = receivedAt;
    }

    public Guid MessageId { get; private set; }

    public string MessageType { get; private set; } = null!;

    public string? Payload { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? LastError { get; private set; }

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
    }

    public void RegisterFailure(string error) => LastError = error;
}
