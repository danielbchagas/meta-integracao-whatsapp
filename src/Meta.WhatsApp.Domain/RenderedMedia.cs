namespace Meta.WhatsApp.Domain;

public sealed class RenderedMedia
{
    private RenderedMedia()
    {
    }

    public RenderedMedia(
        Guid id,
        Guid messageId,
        string storagePath,
        string mimeType,
        string sha256,
        long size,
        int width,
        int height,
        string renderer,
        string rendererVersion,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        if (size <= 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Rendered media dimensions and size must be positive.");
        }

        Id = id;
        MessageId = messageId;
        StoragePath = Required(storagePath, nameof(storagePath));
        MimeType = Required(mimeType, nameof(mimeType));
        Sha256 = Required(sha256, nameof(sha256));
        Size = size;
        Width = width;
        Height = height;
        Renderer = Required(renderer, nameof(renderer));
        RendererVersion = Required(rendererVersion, nameof(rendererVersion));
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid MessageId { get; private set; }

    public string StoragePath { get; private set; } = null!;

    public string MimeType { get; private set; } = null!;

    public string Sha256 { get; private set; } = null!;

    public long Size { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public string Renderer { get; private set; } = null!;

    public string RendererVersion { get; private set; } = null!;

    public string? MetaMediaId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void SetMetaMediaId(string metaMediaId) => MetaMediaId = Required(metaMediaId, nameof(metaMediaId));

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
}
