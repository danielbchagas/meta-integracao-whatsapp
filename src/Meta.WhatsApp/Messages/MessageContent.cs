using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meta.WhatsApp.Messages;

/// <summary>Base type for content accepted by the WhatsApp messages endpoint.</summary>
public abstract record MessageContent
{
    [JsonIgnore]
    public abstract string Type { get; }

    [JsonIgnore]
    public virtual bool RequiresOpenSession => true;

    internal virtual void Validate()
    {
    }
}

public sealed record TextMessageContent : MessageContent
{
    public TextMessageContent(string body, bool previewUrl = false)
    {
        Body = body;
        PreviewUrl = previewUrl;
    }

    public override string Type => "text";

    [JsonPropertyName("body")]
    public string Body { get; }

    [JsonPropertyName("preview_url")]
    public bool PreviewUrl { get; }

    internal override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            throw new ArgumentException("Message body is required.", nameof(Body));
        }
    }
}

public abstract record MediaMessageContent : MessageContent
{
    protected MediaMessageContent(string? id, Uri? link)
    {
        Id = id;
        Link = link;
    }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; }

    [JsonPropertyName("link")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Link { get; }

    internal override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) == (Link is null))
        {
            throw new ArgumentException("Provide exactly one media ID or HTTPS link.");
        }

        if (Link is not null && (!Link.IsAbsoluteUri || Link.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Media link must be an absolute HTTPS URI.", nameof(Link));
        }
    }
}

public sealed record ImageMessageContent : MediaMessageContent
{
    public ImageMessageContent(string? id = null, Uri? link = null, string? caption = null)
        : base(id, link) => Caption = caption;

    public override string Type => "image";

    [JsonPropertyName("caption")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Caption { get; }
}

public sealed record VideoMessageContent : MediaMessageContent
{
    public VideoMessageContent(string? id = null, Uri? link = null, string? caption = null)
        : base(id, link) => Caption = caption;

    public override string Type => "video";

    [JsonPropertyName("caption")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Caption { get; }
}

public sealed record AudioMessageContent : MediaMessageContent
{
    public AudioMessageContent(string? id = null, Uri? link = null)
        : base(id, link)
    {
    }

    public override string Type => "audio";
}

public sealed record DocumentMessageContent : MediaMessageContent
{
    public DocumentMessageContent(
        string? id = null,
        Uri? link = null,
        string? caption = null,
        string? fileName = null)
        : base(id, link)
    {
        Caption = caption;
        FileName = fileName;
    }

    public override string Type => "document";

    [JsonPropertyName("caption")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Caption { get; }

    [JsonPropertyName("filename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; }
}

public sealed record LocationMessageContent(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("address")] string? Address = null) : MessageContent
{
    public override string Type => "location";

    internal override void Validate()
    {
        if (Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(Latitude));
        }

        if (Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(Longitude));
        }
    }
}

/// <summary>
/// Escape hatch for message types added by Meta before a typed model is available in this library.
/// </summary>
public sealed record CustomMessageContent : MessageContent
{
    public CustomMessageContent(string type, JsonElement payload, bool requiresOpenSession = true)
    {
        Type = type;
        Payload = payload;
        RequiresOpenSession = requiresOpenSession;
    }

    public override string Type { get; }

    public override bool RequiresOpenSession { get; }

    [JsonIgnore]
    public JsonElement Payload { get; }

    internal override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Type))
        {
            throw new ArgumentException("Message type is required.", nameof(Type));
        }

        if (Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("Custom payload is required.", nameof(Payload));
        }
    }
}
