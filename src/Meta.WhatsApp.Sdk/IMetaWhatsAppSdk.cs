using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Queries;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Application.Webhooks;

namespace Meta.WhatsApp.Sdk;

public interface IMetaWhatsAppSdk
{
    Task<RegisterWabaResult> RegisterWabaAsync(
        RegisterWabaCommand command,
        CancellationToken cancellationToken = default);

    Task<WabaDetails?> GetWabaAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhoneNumberDetails>> GetPhoneNumbersAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TemplateDetails>> GetTemplatesAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailableTemplateDetails>> GetAvailableTemplatesAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default);

    Task<TemplateDetails?> GetTemplateAsync(
        string metaWabaId,
        Guid templateId,
        CancellationToken cancellationToken = default);

    Task<QueuedOperationResult> QueueTemplateCreateAsync(
        CreateTemplateCommand command,
        CancellationToken cancellationToken = default);

    Task<QueuedOperationResult> QueueTemplateUpdateAsync(
        UpdateTemplateCommand command,
        CancellationToken cancellationToken = default);

    Task<QueuedOperationResult> QueueTemplateDeleteAsync(
        DeleteTemplateCommand command,
        CancellationToken cancellationToken = default);

    Task<OperationDetails?> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<SendNotificationResult> SendNotificationAsync(
        SendNotificationCommand command,
        CancellationToken cancellationToken = default);

    Task<MessageDetails?> GetMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    Task<WebhookIngestionResult> IngestWebhookPayloadAsync(
        ReadOnlyMemory<byte> payload,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}
