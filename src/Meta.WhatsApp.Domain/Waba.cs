namespace Meta.WhatsApp.Domain;

public sealed class Waba
{
    private Waba()
    {
    }

    private Waba(Guid id, string metaWabaId, string credentialKey, DateTimeOffset createdAt)
    {
        Id = id;
        MetaWabaId = Required(metaWabaId, nameof(metaWabaId));
        CredentialKey = Required(credentialKey, nameof(credentialKey));
        Status = WabaStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string MetaWabaId { get; private set; } = null!;

    public string? BusinessId { get; private set; }

    public string? Name { get; private set; }

    public string CredentialKey { get; private set; } = null!;

    public WabaStatus Status { get; private set; }

    public DateTimeOffset? LastSyncAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Waba Register(string metaWabaId, string credentialKey, DateTimeOffset now) =>
        new(Guid.NewGuid(), metaWabaId, credentialKey, now);

    public void Synchronize(
        string? businessId,
        string? name,
        string credentialKey,
        DateTimeOffset synchronizedAt)
    {
        BusinessId = NullIfWhiteSpace(businessId);
        Name = NullIfWhiteSpace(name);
        CredentialKey = Required(credentialKey, nameof(credentialKey));
        Status = WabaStatus.Active;
        LastSyncAt = synchronizedAt;
        UpdatedAt = synchronizedAt;
    }

    public void MarkSyncFailure(WabaStatus status, DateTimeOffset occurredAt)
    {
        if (status is not (WabaStatus.InvalidCredentials or WabaStatus.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "A synchronization failure status is required.");
        }

        Status = status;
        UpdatedAt = occurredAt;
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
