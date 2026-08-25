namespace Meta.WhatsApp.Domain;

public sealed class MessageDefinition
{
    private MessageDefinition()
    {
    }

    public MessageDefinition(
        string code,
        ContentStrategy contentStrategy,
        string? templateName,
        string language,
        string? renderer,
        bool enabled,
        DateTimeOffset now)
    {
        Code = Required(code, nameof(code)).ToUpperInvariant();
        ContentStrategy = contentStrategy;
        Language = Required(language, nameof(language));
        TemplateName = Trim(templateName);
        Renderer = Trim(renderer);
        Enabled = enabled;
        CreatedAt = now;
        UpdatedAt = now;
        ValidateStrategy();
    }

    public string Code { get; private set; } = null!;

    public ContentStrategy ContentStrategy { get; private set; }

    public string? TemplateName { get; private set; }

    public string Language { get; private set; } = null!;

    public string? Renderer { get; private set; }

    public bool Enabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private void ValidateStrategy()
    {
        if (ContentStrategy != ContentStrategy.FreeText && TemplateName is null)
        {
            throw new ArgumentException("TemplateName is required for template strategies.", nameof(TemplateName));
        }

        if (ContentStrategy == ContentStrategy.ImageTemplate && Renderer is null)
        {
            throw new ArgumentException("Renderer is required for image template strategies.", nameof(Renderer));
        }
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
