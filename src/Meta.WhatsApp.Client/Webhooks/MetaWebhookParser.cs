using System.Globalization;
using System.Text;
using System.Text.Json;
using Meta.WhatsApp.Exceptions;

namespace Meta.WhatsApp.Webhooks;

/// <summary>Parses WhatsApp Business Account webhook payloads without depending on ASP.NET.</summary>
public static class MetaWebhookParser
{
    public static MetaWebhookNotification Parse(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        return Parse(Encoding.UTF8.GetBytes(payload));
    }

    public static MetaWebhookNotification Parse(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new MetaWebhookException("The Meta webhook payload is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new MetaWebhookException("The Meta webhook root must be a JSON object.");
            }

            if (root.TryGetProperty("object", out var objectElement) &&
                !string.Equals(
                    objectElement.GetString(),
                    "whatsapp_business_account",
                    StringComparison.Ordinal))
            {
                throw new MetaWebhookException("The payload is not a WhatsApp Business Account webhook.");
            }

            var inboundMessages = new List<InboundWebhookMessage>();
            var statusUpdates = new List<MessageStatusWebhookUpdate>();
            if (!root.TryGetProperty("entry", out var entries))
            {
                return new MetaWebhookNotification(inboundMessages, statusUpdates);
            }

            RequireArray(entries, "entry");
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes))
                {
                    continue;
                }

                RequireArray(changes, "entry[].changes");
                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("field", out var field) ||
                        !string.Equals(field.GetString(), "messages", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var value = GetRequiredProperty(change, "value", "entry[].changes[].value");
                    var channelId = GetChannelId(value);
                    ParseInboundMessages(value, channelId, inboundMessages);
                    ParseStatusUpdates(value, channelId, statusUpdates);
                }
            }

            return new MetaWebhookNotification(inboundMessages, statusUpdates);
        }
        catch (MetaWebhookException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentOutOfRangeException)
        {
            throw new MetaWebhookException("The Meta webhook payload is invalid.", exception);
        }
    }

    private static void ParseInboundMessages(
        JsonElement value,
        string channelId,
        List<InboundWebhookMessage> destination)
    {
        if (!value.TryGetProperty("messages", out var messages))
        {
            return;
        }

        RequireArray(messages, "value.messages");
        foreach (var message in messages.EnumerateArray())
        {
            var contextMessageId = message.TryGetProperty("context", out var context) &&
                context.TryGetProperty("id", out var contextId)
                    ? contextId.GetString()
                    : null;
            destination.Add(new InboundWebhookMessage(
                channelId,
                GetRequiredString(message, "from", "value.messages[].from"),
                GetRequiredString(message, "id", "value.messages[].id"),
                ParseTimestamp(message, "timestamp", "value.messages[].timestamp"),
                GetOptionalString(message, "type") ?? "unknown",
                contextMessageId,
                message.Clone()));
        }
    }

    private static void ParseStatusUpdates(
        JsonElement value,
        string channelId,
        List<MessageStatusWebhookUpdate> destination)
    {
        if (!value.TryGetProperty("statuses", out var statuses))
        {
            return;
        }

        RequireArray(statuses, "value.statuses");
        foreach (var status in statuses.EnumerateArray())
        {
            var (errorCode, errorMessage) = ParseError(status);
            destination.Add(new MessageStatusWebhookUpdate(
                channelId,
                GetRequiredString(status, "recipient_id", "value.statuses[].recipient_id"),
                GetRequiredString(status, "id", "value.statuses[].id"),
                GetRequiredString(status, "status", "value.statuses[].status"),
                ParseTimestamp(status, "timestamp", "value.statuses[].timestamp"),
                errorCode,
                errorMessage,
                status.Clone()));
        }
    }

    private static string GetChannelId(JsonElement value)
    {
        var metadata = GetRequiredProperty(value, "metadata", "value.metadata");
        return GetRequiredString(metadata, "phone_number_id", "value.metadata.phone_number_id");
    }

    private static (string? Code, string? Message) ParseError(JsonElement status)
    {
        if (!status.TryGetProperty("errors", out var errors) ||
            errors.ValueKind != JsonValueKind.Array ||
            errors.GetArrayLength() == 0)
        {
            return (null, null);
        }

        var error = errors[0];
        var code = error.TryGetProperty("code", out var codeElement)
            ? codeElement.ToString()
            : null;
        var message = GetOptionalString(error, "message") ?? GetOptionalString(error, "title");
        if (error.TryGetProperty("error_data", out var errorData))
        {
            message ??= GetOptionalString(errorData, "details");
        }

        return (code, message);
    }

    private static DateTimeOffset ParseTimestamp(
        JsonElement source,
        string propertyName,
        string path)
    {
        var element = GetRequiredProperty(source, propertyName, path);
        var value = element.ValueKind switch
        {
            JsonValueKind.String => long.Parse(
                element.GetString() ?? string.Empty,
                NumberStyles.None,
                CultureInfo.InvariantCulture),
            JsonValueKind.Number => element.GetInt64(),
            _ => throw new MetaWebhookException($"'{path}' must be a Unix timestamp.")
        };
        return DateTimeOffset.FromUnixTimeSeconds(value);
    }

    private static JsonElement GetRequiredProperty(
        JsonElement source,
        string propertyName,
        string path) =>
        source.TryGetProperty(propertyName, out var value)
            ? value
            : throw new MetaWebhookException($"Required webhook property '{path}' is missing.");

    private static string GetRequiredString(
        JsonElement source,
        string propertyName,
        string path)
    {
        var value = GetRequiredProperty(source, propertyName, path);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new MetaWebhookException($"Required webhook property '{path}' is invalid.");
        }

        return value.GetString()!;
    }

    private static string? GetOptionalString(JsonElement source, string propertyName) =>
        source.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void RequireArray(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new MetaWebhookException($"Webhook property '{path}' must be an array.");
        }
    }
}
