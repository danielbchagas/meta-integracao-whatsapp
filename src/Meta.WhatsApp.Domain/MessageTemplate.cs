namespace Meta.WhatsApp.Domain;

public sealed class MessageTemplate
{
    private readonly List<MessageTemplateVersion> _versions = [];

    private MessageTemplate()
    {
    }

    public MessageTemplate(
        Guid id,
        Guid wabaId,
        string? metaTemplateId,
        string name,
        string language,
        string category,
        string status,
        string componentsJson,
        string componentsHash,
        DateTimeOffset now)
    {
        Id = id;
        WabaId = wabaId;
        Name = Required(name, nameof(name));
        Language = Required(language, nameof(language));
        CreatedAt = now;
        ApplySnapshot(metaTemplateId, category, status, componentsJson, componentsHash, now);
    }

    public Guid Id { get; private set; }

    public Guid WabaId { get; private set; }

    public string? MetaTemplateId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Language { get; private set; } = null!;

    public string Category { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public int CurrentVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<MessageTemplateVersion> Versions => _versions;

    public bool ApplySnapshot(
        string? metaTemplateId,
        string category,
        string status,
        string componentsJson,
        string componentsHash,
        DateTimeOffset now)
    {
        MetaTemplateId = Trim(metaTemplateId);
        Category = Required(category, nameof(category)).ToUpperInvariant();
        Status = Required(status, nameof(status)).ToUpperInvariant();
        UpdatedAt = now;

        if (_versions.Count > 0 &&
            string.Equals(_versions[^1].Hash, componentsHash, StringComparison.Ordinal))
        {
            return false;
        }

        CurrentVersion++;
        _versions.Add(new MessageTemplateVersion(
            Guid.NewGuid(),
            Id,
            CurrentVersion,
            Required(componentsJson, nameof(componentsJson)),
            Required(componentsHash, nameof(componentsHash)),
            now));
        return true;
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class MessageTemplateVersion
{
    private MessageTemplateVersion()
    {
    }

    internal MessageTemplateVersion(
        Guid id,
        Guid templateId,
        int version,
        string componentsJson,
        string hash,
        DateTimeOffset createdAt)
    {
        Id = id;
        TemplateId = templateId;
        Version = version;
        ComponentsJson = componentsJson;
        Hash = hash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TemplateId { get; private set; }

    public int Version { get; private set; }

    public string ComponentsJson { get; private set; } = null!;

    public string Hash { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }
}
