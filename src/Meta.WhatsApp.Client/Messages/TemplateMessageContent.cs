using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meta.WhatsApp.Messages;

public sealed record TemplateMessageContent : MessageContent
{
    public TemplateMessageContent(
        string name,
        string languageCode,
        IReadOnlyList<TemplateMessageComponent>? components = null)
    {
        Name = name;
        Language = new TemplateMessageLanguage(languageCode);
        Components = components;
    }

    public override string Type => "template";

    public override bool RequiresOpenSession => false;

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("language")]
    public TemplateMessageLanguage Language { get; }

    [JsonPropertyName("components")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TemplateMessageComponent>? Components { get; }

    internal override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Template name is required.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(Language.Code))
        {
            throw new ArgumentException("Template language code is required.", nameof(Language));
        }
    }
}

public sealed record TemplateMessageLanguage(
    [property: JsonPropertyName("code")] string Code);

public sealed record TemplateMessageComponent
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("sub_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubType { get; init; }

    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Index { get; init; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TemplateMessageParameter>? Parameters { get; init; }
}

/// <summary>
/// Flexible template parameter model. Use <see cref="AdditionalProperties"/> for new Meta parameter shapes.
/// </summary>
public sealed record TemplateMessageParameter
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("payload")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Payload { get; init; }

    [JsonPropertyName("currency")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Currency { get; init; }

    [JsonPropertyName("date_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DateTime { get; init; }

    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Image { get; init; }

    [JsonPropertyName("document")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Document { get; init; }

    [JsonPropertyName("video")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Video { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
