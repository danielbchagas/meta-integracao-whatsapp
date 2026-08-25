using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Queries;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Application.Webhooks;
using Meta.WhatsApp.Webhooks;

namespace Meta.WhatsApp.Sdk;

public sealed class MetaWhatsAppSdk(
    RegisterWabaHandler registerWaba,
    TemplateManagementService templateManagement,
    SendNotificationHandler sendNotification,
    ManagementQueries queries,
    WebhookIngestionService webhookIngestion) : IMetaWhatsAppSdk
{
    public Task<RegisterWabaResult> RegisterWabaAsync(
        RegisterWabaCommand command,
        CancellationToken cancellationToken = default) =>
        registerWaba.HandleAsync(command, cancellationToken);

    public Task<WabaDetails?> GetWabaAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default) =>
        queries.GetWabaAsync(metaWabaId, cancellationToken);

    public Task<IReadOnlyList<PhoneNumberDetails>> GetPhoneNumbersAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default) =>
        queries.GetPhoneNumbersAsync(metaWabaId, cancellationToken);

    public Task<IReadOnlyList<TemplateDetails>> GetTemplatesAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default) =>
        queries.GetTemplatesAsync(metaWabaId, cancellationToken);

    public Task<IReadOnlyList<AvailableTemplateDetails>> GetAvailableTemplatesAsync(
        string metaWabaId,
        CancellationToken cancellationToken = default) =>
        queries.GetAvailableTemplatesAsync(metaWabaId, cancellationToken);

    public Task<TemplateDetails?> GetTemplateAsync(
        string metaWabaId,
        Guid templateId,
        CancellationToken cancellationToken = default) =>
        queries.GetTemplateAsync(metaWabaId, templateId, cancellationToken);

    public Task<QueuedOperationResult> QueueTemplateCreateAsync(
        CreateTemplateCommand command,
        CancellationToken cancellationToken = default) =>
        templateManagement.QueueCreateAsync(command, cancellationToken);

    public Task<QueuedOperationResult> QueueTemplateUpdateAsync(
        UpdateTemplateCommand command,
        CancellationToken cancellationToken = default) =>
        templateManagement.QueueUpdateAsync(command, cancellationToken);

    public Task<QueuedOperationResult> QueueTemplateDeleteAsync(
        DeleteTemplateCommand command,
        CancellationToken cancellationToken = default) =>
        templateManagement.QueueDeleteAsync(command, cancellationToken);

    public Task<OperationDetails?> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        queries.GetOperationAsync(operationId, cancellationToken);

    public Task<SendNotificationResult> SendNotificationAsync(
        SendNotificationCommand command,
        CancellationToken cancellationToken = default) =>
        sendNotification.HandleAsync(command, cancellationToken);

    public Task<MessageDetails?> GetMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default) =>
        queries.GetMessageAsync(messageId, cancellationToken);

    public Task<WebhookIngestionResult> IngestWebhookPayloadAsync(
        ReadOnlyMemory<byte> payload,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var notification = MetaWebhookParser.Parse(payload);
        var events = notification.InboundMessages
            .Select(message => new IncomingWebhookEvent(
                "message",
                message.ChannelId,
                message.MessageId,
                message.Recipient,
                Status: null,
                message.ReceivedAtUtc,
                message.Payload.GetRawText()))
            .Concat(notification.StatusUpdates.Select(status => new IncomingWebhookEvent(
                "status",
                status.ChannelId,
                status.MessageId,
                status.Recipient,
                status.Status,
                status.OccurredAtUtc,
                status.Payload.GetRawText(),
                status.ErrorCode,
                status.ErrorMessage)))
            .ToArray();
        return webhookIngestion.IngestAsync(events, correlationId, cancellationToken);
    }
}
