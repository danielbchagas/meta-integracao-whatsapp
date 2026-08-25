using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Templates;

public sealed record CreateTemplateCommand(
    string MetaWabaId,
    string Name,
    string Language,
    string Category,
    JsonElement Components,
    Guid? CorrelationId = null);

public sealed record UpdateTemplateCommand(
    string MetaWabaId,
    Guid TemplateId,
    string Category,
    JsonElement Components,
    Guid? CorrelationId = null);

public sealed record DeleteTemplateCommand(
    string MetaWabaId,
    Guid TemplateId,
    Guid? CorrelationId = null);

public sealed record QueuedOperationResult(Guid OperationId, OperationStatus Status);

public sealed class TemplateManagementService
{
    private readonly IWabaRepository _wabas;
    private readonly ITemplateRepository _templates;
    private readonly IOperationRepository _operations;
    private readonly IOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public TemplateManagementService(
        IWabaRepository wabas,
        ITemplateRepository templates,
        IOperationRepository operations,
        IOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _wabas = wabas;
        _templates = templates;
        _operations = operations;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<QueuedOperationResult> QueueCreateAsync(
        CreateTemplateCommand command,
        CancellationToken cancellationToken)
    {
        var waba = await RequiredWabaAsync(command.MetaWabaId, cancellationToken).ConfigureAwait(false);
        var duplicate = await _templates
            .GetByNaturalKeyAsync(waba.Id, command.Name, command.Language, cancellationToken)
            .ConfigureAwait(false);
        if (duplicate is not null)
        {
            throw new InvalidOperationException("A template with the same name and language already exists.");
        }

        return await QueueAsync(
                waba,
                OperationType.CreateTemplate,
                template: null,
                command.Name,
                command.Language,
                command.Category,
                command.Components.GetRawText(),
                command.CorrelationId,
                IntegrationEventTypes.TemplateCreateRequested,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<QueuedOperationResult> QueueUpdateAsync(
        UpdateTemplateCommand command,
        CancellationToken cancellationToken)
    {
        var waba = await RequiredWabaAsync(command.MetaWabaId, cancellationToken).ConfigureAwait(false);
        var template = await RequiredTemplateAsync(command.TemplateId, waba.Id, cancellationToken).ConfigureAwait(false);
        return await QueueAsync(
                waba,
                OperationType.UpdateTemplate,
                template,
                template.Name,
                template.Language,
                command.Category,
                command.Components.GetRawText(),
                command.CorrelationId,
                IntegrationEventTypes.TemplateUpdateRequested,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<QueuedOperationResult> QueueDeleteAsync(
        DeleteTemplateCommand command,
        CancellationToken cancellationToken)
    {
        var waba = await RequiredWabaAsync(command.MetaWabaId, cancellationToken).ConfigureAwait(false);
        var template = await RequiredTemplateAsync(command.TemplateId, waba.Id, cancellationToken).ConfigureAwait(false);
        return await QueueAsync(
                waba,
                OperationType.DeleteTemplate,
                template,
                template.Name,
                template.Language,
                category: null,
                componentsJson: null,
                command.CorrelationId,
                IntegrationEventTypes.TemplateDeleteRequested,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<QueuedOperationResult> QueueAsync(
        Waba waba,
        OperationType operationType,
        MessageTemplate? template,
        string? name,
        string? language,
        string? category,
        string? componentsJson,
        Guid? requestedCorrelationId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var correlationId = requestedCorrelationId ?? Guid.NewGuid();
        var operation = new IntegrationOperation(
            Guid.NewGuid(),
            waba.Id,
            operationType,
            template?.Id,
            now,
            correlationId);
        var integrationEvent = new TemplateMutationRequestedEvent(
            operation.Id,
            waba.Id,
            operationType,
            template?.Id,
            name,
            language,
            category,
            componentsJson);

        await _unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                _operations.Add(operation);
                _outbox.Add(new OutboxMessage(
                    Guid.NewGuid(),
                    nameof(MessageTemplate),
                    template?.Id.ToString("D") ?? operation.Id.ToString("D"),
                    eventType,
                    ApplicationJson.Serialize(integrationEvent),
                    now,
                    correlationId));
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        return new QueuedOperationResult(operation.Id, operation.Status);
    }

    private async Task<Waba> RequiredWabaAsync(string metaWabaId, CancellationToken cancellationToken) =>
        await _wabas.GetByMetaIdAsync(metaWabaId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"WABA '{metaWabaId}' was not found.");

    private async Task<MessageTemplate> RequiredTemplateAsync(
        Guid id,
        Guid wabaId,
        CancellationToken cancellationToken)
    {
        var template = await _templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Template '{id}' was not found.");
        return template.WabaId == wabaId
            ? template
            : throw new KeyNotFoundException($"Template '{id}' does not belong to the requested WABA.");
    }
}
