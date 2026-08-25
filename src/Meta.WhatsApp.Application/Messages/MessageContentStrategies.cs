using System.Text.Json;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Messages;

public sealed record PreparedMetaMessage(string Type, object Content);

public interface IMessageContentStrategy
{
    ContentStrategy Strategy { get; }

    PreparedMetaMessage Prepare(
        MessageDefinition definition,
        WhatsAppMessage message,
        RenderedMedia? media);
}

public sealed class MessageContentStrategyResolver(IEnumerable<IMessageContentStrategy> strategies)
{
    private readonly Dictionary<ContentStrategy, IMessageContentStrategy> _strategies = strategies
        .ToDictionary(item => item.Strategy);

    public IMessageContentStrategy Resolve(ContentStrategy strategy) =>
        _strategies.TryGetValue(strategy, out var implementation)
            ? implementation
            : throw new InvalidOperationException($"No message strategy is registered for '{strategy}'.");
}

public sealed class FreeTextContentStrategy : IMessageContentStrategy
{
    public ContentStrategy Strategy => ContentStrategy.FreeText;

    public PreparedMetaMessage Prepare(
        MessageDefinition definition,
        WhatsAppMessage message,
        RenderedMedia? media)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(message);
        using var document = JsonDocument.Parse(message.PayloadJson);
        if (!document.RootElement.TryGetProperty("text", out var textElement) ||
            textElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(textElement.GetString()))
        {
            throw new ArgumentException("Free-text payload requires a non-empty 'text' property.", nameof(message));
        }

        var previewUrl = document.RootElement.TryGetProperty("previewUrl", out var previewElement) &&
            previewElement.ValueKind is JsonValueKind.True;
        return new PreparedMetaMessage(
            "text",
            new { body = textElement.GetString(), preview_url = previewUrl });
    }
}

public sealed class TextTemplateContentStrategy : IMessageContentStrategy
{
    public ContentStrategy Strategy => ContentStrategy.TextTemplate;

    public PreparedMetaMessage Prepare(
        MessageDefinition definition,
        WhatsAppMessage message,
        RenderedMedia? media)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(message);
        return new PreparedMetaMessage(
            "template",
            BuildTemplate(definition, message.PayloadJson, header: null));
    }

    internal static object BuildTemplate(
        MessageDefinition definition,
        string payloadJson,
        object? header)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var components = new List<object>();
        if (header is not null)
        {
            components.Add(header);
        }

        if (document.RootElement.TryGetProperty("components", out var explicitComponents))
        {
            if (explicitComponents.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("'components' must be a JSON array.", nameof(payloadJson));
            }

            components.AddRange(explicitComponents.EnumerateArray().Select(item => (object)item.Clone()));
        }
        else if (document.RootElement.TryGetProperty("parameters", out var parameters))
        {
            if (parameters.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("'parameters' must be a JSON array.", nameof(payloadJson));
            }

            components.Add(new
            {
                type = "body",
                parameters = parameters.EnumerateArray()
                    .Select(item => new { type = "text", text = ToParameterText(item) })
                    .ToArray()
            });
        }

        return new
        {
            name = definition.TemplateName,
            language = new { code = definition.Language },
            components
        };
    }

    private static string ToParameterText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
        _ => throw new ArgumentException("Template parameters must be strings, numbers, or booleans.", nameof(element))
    };
}

public sealed class ImageTemplateContentStrategy : IMessageContentStrategy
{
    public ContentStrategy Strategy => ContentStrategy.ImageTemplate;

    public PreparedMetaMessage Prepare(
        MessageDefinition definition,
        WhatsAppMessage message,
        RenderedMedia? media)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(message);
        if (string.IsNullOrWhiteSpace(media?.MetaMediaId))
        {
            throw new InvalidOperationException("Image template requires uploaded Meta media.");
        }

        var header = new
        {
            type = "header",
            parameters = new[]
            {
                new { type = "image", image = new { id = media.MetaMediaId } }
            }
        };
        return new PreparedMetaMessage(
            "template",
            TextTemplateContentStrategy.BuildTemplate(definition, message.PayloadJson, header));
    }
}
