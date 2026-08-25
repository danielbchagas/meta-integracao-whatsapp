namespace Meta.WhatsApp.Domain;

public sealed class IntegrationOperation
{
    private IntegrationOperation()
    {
    }

    public IntegrationOperation(
        Guid id,
        Guid wabaId,
        OperationType operationType,
        Guid? entityId,
        DateTimeOffset createdAt,
        Guid correlationId)
    {
        Id = id;
        WabaId = wabaId;
        OperationType = operationType;
        EntityId = entityId;
        Status = OperationStatus.Pending;
        CreatedAt = createdAt;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }

    public Guid WabaId { get; private set; }

    public OperationType OperationType { get; private set; }

    public Guid? EntityId { get; private set; }

    public OperationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid CorrelationId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Start(DateTimeOffset now)
    {
        if (Status is not (OperationStatus.Pending or OperationStatus.Failed))
        {
            throw new InvalidOperationException($"Operation in state {Status} cannot be started.");
        }

        Status = OperationStatus.Processing;
        Attempts++;
        NextAttemptAt = null;
        LastError = null;
    }

    public void WaitForMeta() => Status = OperationStatus.WaitingMeta;

    public void AttachEntity(Guid entityId)
    {
        if (EntityId is not null && EntityId != entityId)
        {
            throw new InvalidOperationException("Operation is already attached to another entity.");
        }

        EntityId = entityId;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OperationStatus.Succeeded;
        CompletedAt = now;
        NextAttemptAt = null;
        LastError = null;
    }

    public void Fail(string error, DateTimeOffset? retryAt, bool permanent, DateTimeOffset now)
    {
        LastError = string.IsNullOrWhiteSpace(error) ? "Unknown integration failure." : error;
        NextAttemptAt = permanent ? null : retryAt;
        Status = permanent ? OperationStatus.FailedPermanent : OperationStatus.Failed;
        CompletedAt = permanent ? now : null;
    }
}
