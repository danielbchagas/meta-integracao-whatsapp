using System.Text.Json;
using System.Text.Json.Serialization;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application;

public static class IntegrationEventTypes
{
    public const string WabaSynchronized = "whatsapp.waba.synchronized.v1";
    public const string TemplateCreateRequested = "whatsapp.template.create-requested.v1";
    public const string TemplateUpdateRequested = "whatsapp.template.update-requested.v1";
    public const string TemplateDeleteRequested = "whatsapp.template.delete-requested.v1";
    public const string MessageRequested = "whatsapp.message.requested.v1";
    public const string MessageStatusChanged = "whatsapp.message.status-changed.v1";
    public const string WebhookReceived = "whatsapp.webhook.received.v1";
    public const string InboundMessageReceived = "whatsapp.inbound-message.received.v1";
}

public static class ApplicationJson
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SerializerOptions);
}

public sealed record WabaSynchronizedEvent(
    Guid WabaId,
    string MetaWabaId,
    int PhoneNumberCount,
    int TemplateCount,
    DateTimeOffset SynchronizedAt);

public sealed record TemplateMutationRequestedEvent(
    Guid OperationId,
    Guid WabaId,
    OperationType OperationType,
    Guid? TemplateId,
    string? TemplateName,
    string? Language,
    string? Category,
    string? ComponentsJson);

public sealed record MessageRequestedEvent(Guid MessageId, Guid OperationId);

public sealed record MessageStatusChangedEvent(
    Guid MessageId,
    WhatsAppMessageStatus Status,
    string? MetaMessageId,
    DateTimeOffset OccurredAt,
    string? Error);

public sealed record WebhookReceivedEvent(
    Guid InboxMessageId,
    string Kind,
    string ChannelId,
    string MetaObjectId,
    string? Recipient,
    string? Status,
    DateTimeOffset OccurredAt,
    string PayloadJson,
    string? ErrorCode,
    string? ErrorMessage);
