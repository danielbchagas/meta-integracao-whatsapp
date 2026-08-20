using System.Text.Json.Serialization;
using Meta.WhatsApp.Templates;

namespace Meta.WhatsApp.Internal;

internal sealed record SendMessageResponse
{
    [JsonPropertyName("contacts")]
    public IReadOnlyList<MessageContact>? Contacts { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<MessageIdentifier> Messages { get; init; }
}

internal sealed record MessageContact
{
    [JsonPropertyName("input")]
    public string? Input { get; init; }

    [JsonPropertyName("wa_id")]
    public string? WhatsAppId { get; init; }
}

internal sealed record MessageIdentifier
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

internal sealed record TemplateListResponse
{
    [JsonPropertyName("data")]
    public IReadOnlyList<WhatsAppTemplate> Data { get; init; } = [];

    [JsonPropertyName("paging")]
    public GraphPaging? Paging { get; init; }
}

internal sealed record GraphPaging
{
    [JsonPropertyName("cursors")]
    public GraphCursors? Cursors { get; init; }

    [JsonPropertyName("next")]
    public string? Next { get; init; }
}

internal sealed record GraphCursors
{
    [JsonPropertyName("after")]
    public string? After { get; init; }
}

internal sealed record CreateTemplateResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }
}

internal sealed record SuccessResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }
}

internal sealed record GraphErrorEnvelope
{
    [JsonPropertyName("error")]
    public GraphError? Error { get; init; }
}

internal sealed record GraphError
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("code")]
    public int? Code { get; init; }

    [JsonPropertyName("error_subcode")]
    public int? ErrorSubcode { get; init; }

    [JsonPropertyName("fbtrace_id")]
    public string? TraceId { get; init; }
}
