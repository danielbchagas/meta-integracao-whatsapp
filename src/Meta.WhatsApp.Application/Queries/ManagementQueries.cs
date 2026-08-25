using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Queries;

public sealed record WabaDetails(
    Guid Id,
    string MetaWabaId,
    string? BusinessId,
    string? Name,
    WabaStatus Status,
    DateTimeOffset? LastSyncAt);

public sealed record PhoneNumberDetails(
    Guid Id,
    string MetaPhoneNumberId,
    string DisplayPhoneNumber,
    string? VerifiedName,
    string? QualityRating,
    string? PlatformType,
    string? Status);

public sealed record TemplateDetails(
    Guid Id,
    string? MetaTemplateId,
    string Name,
    string Language,
    string Category,
    string Status,
    int CurrentVersion,
    string ComponentsJson,
    DateTimeOffset UpdatedAt);

public sealed record AvailableTemplateDetails(
    Guid Id,
    string? MetaTemplateId,
    string Name,
    string Language,
    string Category,
    IReadOnlyList<string> MessageTypes);

public sealed record OperationDetails(
    Guid Id,
    Guid WabaId,
    OperationType OperationType,
    Guid? EntityId,
    OperationStatus Status,
    int Attempts,
    DateTimeOffset? NextAttemptAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Guid CorrelationId);

public sealed record MessageDetails(
    Guid Id,
    string IdempotencyKey,
    Guid WabaId,
    Guid PhoneNumberId,
    string Recipient,
    string MessageType,
    ContentStrategy ContentStrategy,
    string? TemplateName,
    string? MetaMessageId,
    WhatsAppMessageStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? ReadAt,
    string? LastError,
    Guid CorrelationId);

public sealed class ManagementQueries
{
    private readonly IWabaRepository _wabas;
    private readonly IPhoneNumberRepository _phoneNumbers;
    private readonly ITemplateRepository _templates;
    private readonly IMessageDefinitionRepository _definitions;
    private readonly IOperationRepository _operations;
    private readonly IMessageRepository _messages;

    public ManagementQueries(
        IWabaRepository wabas,
        IPhoneNumberRepository phoneNumbers,
        ITemplateRepository templates,
        IMessageDefinitionRepository definitions,
        IOperationRepository operations,
        IMessageRepository messages)
    {
        _wabas = wabas;
        _phoneNumbers = phoneNumbers;
        _templates = templates;
        _definitions = definitions;
        _operations = operations;
        _messages = messages;
    }

    public async Task<WabaDetails?> GetWabaAsync(string metaWabaId, CancellationToken cancellationToken)
    {
        var waba = await _wabas.GetByMetaIdAsync(metaWabaId, cancellationToken).ConfigureAwait(false);
        return waba is null
            ? null
            : new WabaDetails(
                waba.Id,
                waba.MetaWabaId,
                waba.BusinessId,
                waba.Name,
                waba.Status,
                waba.LastSyncAt);
    }

    public async Task<IReadOnlyList<PhoneNumberDetails>> GetPhoneNumbersAsync(
        string metaWabaId,
        CancellationToken cancellationToken)
    {
        var waba = await _wabas.GetByMetaIdAsync(metaWabaId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"WABA '{metaWabaId}' was not found.");
        var numbers = await _phoneNumbers.GetByWabaIdAsync(waba.Id, cancellationToken).ConfigureAwait(false);
        return numbers.Select(item => new PhoneNumberDetails(
                item.Id,
                item.MetaPhoneNumberId,
                item.DisplayPhoneNumber,
                item.VerifiedName,
                item.QualityRating,
                item.PlatformType,
                item.Status))
            .ToArray();
    }

    public async Task<IReadOnlyList<TemplateDetails>> GetTemplatesAsync(
        string metaWabaId,
        CancellationToken cancellationToken)
    {
        var waba = await _wabas.GetByMetaIdAsync(metaWabaId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"WABA '{metaWabaId}' was not found.");
        var templates = await _templates.GetByWabaIdAsync(waba.Id, cancellationToken).ConfigureAwait(false);
        return templates.Select(ToDetails).ToArray();
    }

    public async Task<TemplateDetails?> GetTemplateAsync(
        string metaWabaId,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var waba = await _wabas.GetByMetaIdAsync(metaWabaId, cancellationToken).ConfigureAwait(false);
        if (waba is null)
        {
            return null;
        }

        var template = await _templates.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
        return template is null || template.WabaId != waba.Id ? null : ToDetails(template);
    }

    public async Task<IReadOnlyList<AvailableTemplateDetails>> GetAvailableTemplatesAsync(
        string metaWabaId,
        CancellationToken cancellationToken)
    {
        var waba = await _wabas.GetByMetaIdAsync(metaWabaId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"WABA '{metaWabaId}' was not found.");
        var templates = await _templates.GetByWabaIdAsync(waba.Id, cancellationToken).ConfigureAwait(false);
        var definitions = await _definitions.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return templates
            .Where(item => string.Equals(item.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            .Select(item => new AvailableTemplateDetails(
                item.Id,
                item.MetaTemplateId,
                item.Name,
                item.Language,
                item.Category,
                definitions
                    .Where(definition =>
                        definition.Enabled &&
                        string.Equals(definition.TemplateName, item.Name, StringComparison.Ordinal) &&
                        string.Equals(definition.Language, item.Language, StringComparison.OrdinalIgnoreCase))
                    .Select(definition => definition.Code)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Language, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<OperationDetails?> GetOperationAsync(Guid id, CancellationToken cancellationToken)
    {
        var operation = await _operations.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return operation is null
            ? null
            : new OperationDetails(
                operation.Id,
                operation.WabaId,
                operation.OperationType,
                operation.EntityId,
                operation.Status,
                operation.Attempts,
                operation.NextAttemptAt,
                operation.LastError,
                operation.CreatedAt,
                operation.CompletedAt,
                operation.CorrelationId);
    }

    public async Task<MessageDetails?> GetMessageAsync(Guid id, CancellationToken cancellationToken)
    {
        var message = await _messages.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return message is null
            ? null
            : new MessageDetails(
                message.Id,
                message.IdempotencyKey,
                message.WabaId,
                message.PhoneNumberId,
                message.Recipient,
                message.MessageType,
                message.ContentStrategy,
                message.TemplateName,
                message.MetaMessageId,
                message.Status,
                message.CreatedAt,
                message.SentAt,
                message.DeliveredAt,
                message.ReadAt,
                message.LastError,
                message.CorrelationId);
    }

    private static TemplateDetails ToDetails(MessageTemplate messageTemplate)
    {
        var currentVersion = messageTemplate.Versions.OrderByDescending(item => item.Version).First();
        return new TemplateDetails(
            messageTemplate.Id,
            messageTemplate.MetaTemplateId,
            messageTemplate.Name,
            messageTemplate.Language,
            messageTemplate.Category,
            messageTemplate.Status,
            messageTemplate.CurrentVersion,
            currentVersion.ComponentsJson,
            messageTemplate.UpdatedAt);
    }
}
