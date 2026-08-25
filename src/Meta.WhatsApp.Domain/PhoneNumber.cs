namespace Meta.WhatsApp.Domain;

public sealed class PhoneNumber
{
    private PhoneNumber()
    {
    }

    public PhoneNumber(
        Guid id,
        Guid wabaId,
        string metaPhoneNumberId,
        string displayPhoneNumber,
        string? verifiedName,
        string? qualityRating,
        string? platformType,
        string? status,
        DateTimeOffset now)
    {
        Id = id;
        WabaId = wabaId;
        MetaPhoneNumberId = Required(metaPhoneNumberId, nameof(metaPhoneNumberId));
        CreatedAt = now;
        ApplySnapshot(displayPhoneNumber, verifiedName, qualityRating, platformType, status, now);
    }

    public Guid Id { get; private set; }

    public Guid WabaId { get; private set; }

    public string MetaPhoneNumberId { get; private set; } = null!;

    public string DisplayPhoneNumber { get; private set; } = null!;

    public string? VerifiedName { get; private set; }

    public string? QualityRating { get; private set; }

    public string? PlatformType { get; private set; }

    public string? Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void ApplySnapshot(
        string displayPhoneNumber,
        string? verifiedName,
        string? qualityRating,
        string? platformType,
        string? status,
        DateTimeOffset now)
    {
        DisplayPhoneNumber = Required(displayPhoneNumber, nameof(displayPhoneNumber));
        VerifiedName = Trim(verifiedName);
        QualityRating = Trim(qualityRating);
        PlatformType = Trim(platformType);
        Status = Trim(status);
        UpdatedAt = now;
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
