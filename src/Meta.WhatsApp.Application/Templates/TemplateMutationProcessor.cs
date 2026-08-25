using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Templates;

public sealed record TemplateMutationResult(Guid OperationId, OperationStatus Status, Guid? TemplateId);

public sealed class TemplateMutationProcessor
{
    private const int MaxAttempts = 10;

    private readonly IWabaRepository _wabas;
    private readonly ITemplateRepository _templates;
    private readonly IOperationRepository _operations;
    private readonly ITemplateMetaClient _metaClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public TemplateMutationProcessor(
        IWabaRepository wabas,
        ITemplateRepository templates,
        IOperationRepository operations,
        ITemplateMetaClient metaClient,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _wabas = wabas;
        _templates = templates;
        _operations = operations;
        _metaClient = metaClient;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<TemplateMutationResult> ProcessAsync(
        TemplateMutationRequestedEvent requestedEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedEvent);
        var operation = await _operations.GetByIdAsync(requestedEvent.OperationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Operation '{requestedEvent.OperationId}' was not found.");
        if (operation.Status == OperationStatus.Succeeded)
        {
            return new TemplateMutationResult(operation.Id, operation.Status, operation.EntityId);
        }

        var waba = await _wabas.GetByIdAsync(requestedEvent.WabaId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"WABA '{requestedEvent.WabaId}' was not found.");
        var context = new MetaApiContext(waba.CredentialKey);
        operation.Start(_timeProvider.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Guid? templateId = requestedEvent.OperationType switch
            {
                OperationType.CreateTemplate => await CreateAsync(
                    context,
                    waba,
                    requestedEvent,
                    cancellationToken).ConfigureAwait(false),
                OperationType.UpdateTemplate => await UpdateAsync(
                    context,
                    requestedEvent,
                    cancellationToken).ConfigureAwait(false),
                OperationType.DeleteTemplate => await DeleteAsync(
                    context,
                    waba,
                    requestedEvent,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException(
                    $"Operation '{requestedEvent.OperationType}' is not a template mutation.")
            };

            if (templateId is { } attachedTemplateId)
            {
                operation.AttachEntity(attachedTemplateId);
            }

            operation.Complete(_timeProvider.GetUtcNow());
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new TemplateMutationResult(operation.Id, operation.Status, templateId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var now = _timeProvider.GetUtcNow();
            var retryable = exception is MetaApiException { Retryable: true } && operation.Attempts < MaxAttempts;
            var retryAt = retryable ? now.Add(GetRetryDelay(operation.Attempts, exception)) : (DateTimeOffset?)null;
            operation.Fail(exception.Message, retryAt, !retryable, now);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            if (retryable)
            {
                throw new MessageProcessingException(
                    $"Template operation '{operation.Id}' failed and can be retried.",
                    retryAt!.Value,
                    exception);
            }

            return new TemplateMutationResult(operation.Id, operation.Status, operation.EntityId);
        }
    }

    private async Task<Guid> CreateAsync(
        MetaApiContext context,
        Waba waba,
        TemplateMutationRequestedEvent requestedEvent,
        CancellationToken cancellationToken)
    {
        var writeRequest = CreateWriteRequest(requestedEvent);
        var result = await _metaClient
            .CreateAsync(context, waba.MetaWabaId, writeRequest, cancellationToken)
            .ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var componentsJson = requestedEvent.ComponentsJson!;
        var messageTemplate = new MessageTemplate(
            Guid.NewGuid(),
            waba.Id,
            result.Id,
            requestedEvent.TemplateName!,
            requestedEvent.Language!,
            result.Category ?? requestedEvent.Category!,
            result.Status ?? "PENDING",
            componentsJson,
            Hash(componentsJson),
            now);
        _templates.Add(messageTemplate);
        return messageTemplate.Id;
    }

    private async Task<Guid> UpdateAsync(
        MetaApiContext context,
        TemplateMutationRequestedEvent requestedEvent,
        CancellationToken cancellationToken)
    {
        var template = await RequiredTemplateAsync(requestedEvent, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(template.MetaTemplateId))
        {
            throw new InvalidOperationException("Template has no Meta identifier and cannot be updated.");
        }

        await _metaClient
            .UpdateAsync(context, template.MetaTemplateId, CreateWriteRequest(requestedEvent), cancellationToken)
            .ConfigureAwait(false);
        template.ApplySnapshot(
            template.MetaTemplateId,
            requestedEvent.Category!,
            "PENDING",
            requestedEvent.ComponentsJson!,
            Hash(requestedEvent.ComponentsJson!),
            _timeProvider.GetUtcNow());
        return template.Id;
    }

    private async Task<Guid> DeleteAsync(
        MetaApiContext context,
        Waba waba,
        TemplateMutationRequestedEvent requestedEvent,
        CancellationToken cancellationToken)
    {
        var template = await RequiredTemplateAsync(requestedEvent, cancellationToken).ConfigureAwait(false);
        await _metaClient
            .DeleteAsync(context, waba.MetaWabaId, template.Name, cancellationToken)
            .ConfigureAwait(false);
        _templates.Remove(template);
        return template.Id;
    }

    private async Task<MessageTemplate> RequiredTemplateAsync(
        TemplateMutationRequestedEvent requestedEvent,
        CancellationToken cancellationToken) =>
        requestedEvent.TemplateId is { } id
            ? await _templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Template '{id}' was not found.")
            : throw new InvalidOperationException("Template ID is required for this operation.");

    private static MetaTemplateWriteRequest CreateWriteRequest(TemplateMutationRequestedEvent requestedEvent)
    {
        using var document = JsonDocument.Parse(requestedEvent.ComponentsJson!);
        return new MetaTemplateWriteRequest(
            requestedEvent.TemplateName!,
            requestedEvent.Language!,
            requestedEvent.Category!,
            document.RootElement.Clone());
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static TimeSpan GetRetryDelay(int attempts, Exception exception)
    {
        if (exception is MetaApiException { RetryAfter: { } retryAfter })
        {
            return retryAfter;
        }

        return TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, attempts - 1))));
    }
}
