using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meta.WhatsApp.Templates;

/// <summary>Definition submitted when a WhatsApp message template is created or synchronized.</summary>
public sealed record TemplateDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("language")]
    public required string Language { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("components")]
    public required IReadOnlyList<TemplateComponent> Components { get; init; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("Template name is required.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(Language))
        {
            throw new ArgumentException("Template language is required.", nameof(Language));
        }

        if (string.IsNullOrWhiteSpace(Category))
        {
            throw new ArgumentException("Template category is required.", nameof(Category));
        }

        if (Components is null || Components.Count == 0)
        {
            throw new ArgumentException("At least one template component is required.", nameof(Components));
        }
    }
}

public sealed record TemplateComponent
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("example")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Example { get; init; }

    [JsonPropertyName("buttons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TemplateButton>? Buttons { get; init; }

    /// <summary>Supports component fields introduced by newer Graph API versions.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record TemplateButton
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }

    [JsonPropertyName("phone_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhoneNumber { get; init; }

    [JsonPropertyName("example")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Example { get; init; }

    /// <summary>Supports button fields introduced by newer Graph API versions.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

/// <summary>Template returned by the WhatsApp Business Management API.</summary>
public sealed record WhatsAppTemplate
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("language")]
    public required string Language { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("components")]
    public required IReadOnlyList<TemplateComponent> Components { get; init; }
}

public sealed record CreateTemplateResult(
    string Id,
    string Status,
    string Category);

public enum TemplateSynchronizationAction
{
    Created,
    Updated,
    Unchanged
}

/// <summary>Result of idempotently creating or updating a template.</summary>
public sealed record TemplateSynchronizationResult(
    TemplateSynchronizationAction Action,
    string TemplateId,
    string? Status,
    string Category);
