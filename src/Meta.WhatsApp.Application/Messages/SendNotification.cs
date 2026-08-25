using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Messages;

public sealed record SendNotificationCommand(
    string MetaWabaId,
    string MetaPhoneNumberId,
    string MessageType,
    string Recipient,
    JsonElement Data,
    string IdempotencyKey,
    Guid? CorrelationId = null);

public sealed record SendNotificationResult(
    Guid MessageId,
    WhatsAppMessageStatus Status,
    bool Duplicate,
    Guid CorrelationId);

public sealed class SendNotificationHandler
{
    private readonly IWabaRepository _wabas;
    private readonly IPhoneNumberRepository _phoneNumbers;
    private readonly IMessageDefinitionRepository _definitions;
    private readonly ITemplateRepository _templates;
    private readonly IMessageRepository _messages;
    private readonly IOperationRepository _operations;
    private readonly IOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public SendNotificationHandler(
        IWabaRepository wabas,
        IPhoneNumberRepository phoneNumbers,
        IMessageDefinitionRepository definitions,
        ITemplateRepository templates,
        IMessageRepository messages,
        IOperationRepository operations,
        IOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _wabas = wabas;
        _phoneNumbers = phoneNumbers;
        _definitions = definitions;
        _templates = templates;
        _messages = messages;
        _operations = operations;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<SendNotificationResult> HandleAsync(
        SendNotificationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var idempotencyKey = Required(command.IdempotencyKey, nameof(command.IdempotencyKey));
        var existing = await _messages
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new SendNotificationResult(existing.Id, existing.Status, true, existing.CorrelationId);
        }

        var waba = await _wabas.GetByMetaIdAsync(command.MetaWabaId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"WABA '{command.MetaWabaId}' was not found.");
        if (waba.Status != WabaStatus.Active)
        {
            throw new InvalidOperationException($"WABA '{command.MetaWabaId}' is not active.");
        }

        var phoneNumber = await _phoneNumbers
            .GetByMetaIdAsync(command.MetaPhoneNumberId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Phone number '{command.MetaPhoneNumberId}' was not found.");
        if (phoneNumber.WabaId != waba.Id)
        {
            throw new InvalidOperationException("The phone number does not belong to the requested WABA.");
        }

        var messageType = Required(command.MessageType, nameof(command.MessageType)).ToUpperInvariant();
        var definition = await _definitions.GetAsync(messageType, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Message definition '{messageType}' was not found.");
        if (!definition.Enabled)
        {
            throw new InvalidOperationException($"Message definition '{messageType}' is disabled.");
        }

        if (definition.ContentStrategy != ContentStrategy.FreeText)
        {
            var template = await _templates
                .GetByNaturalKeyAsync(waba.Id, definition.TemplateName!, definition.Language, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(template?.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Template '{definition.TemplateName}' ({definition.Language}) is not approved.");
            }
        }

        var now = _timeProvider.GetUtcNow();
        var correlationId = command.CorrelationId ?? Guid.NewGuid();
        var message = new WhatsAppMessage(
            Guid.NewGuid(),
            idempotencyKey,
            waba.Id,
            phoneNumber.Id,
            command.Recipient,
            messageType,
            definition.ContentStrategy,
            command.Data.GetRawText(),
            definition.TemplateName,
            now,
            correlationId);
        var operation = new IntegrationOperation(
            Guid.NewGuid(),
            waba.Id,
            OperationType.SendMessage,
            message.Id,
            now,
            correlationId);

        await _unitOfWork.ExecuteInTransactionAsync(
            _ =>
            {
                _messages.Add(message);
                _operations.Add(operation);
                _outbox.Add(new OutboxMessage(
                    Guid.NewGuid(),
                    nameof(WhatsAppMessage),
                    message.Id.ToString("D"),
                    IntegrationEventTypes.MessageRequested,
                    ApplicationJson.Serialize(new MessageRequestedEvent(message.Id, operation.Id)),
                    now,
                    correlationId));
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        return new SendNotificationResult(message.Id, message.Status, false, correlationId);
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
}
