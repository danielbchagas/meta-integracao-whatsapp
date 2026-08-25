using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Messages;

public sealed record MessageProcessingResult(
    Guid MessageId,
    WhatsAppMessageStatus Status,
    string? MetaMessageId,
    bool AlreadyProcessed);

public sealed class MessageProcessingException : Exception
{
    public MessageProcessingException(string message, DateTimeOffset retryAt, Exception innerException)
        : base(message, innerException) => RetryAt = retryAt;

    public DateTimeOffset RetryAt { get; }
}

public sealed class MessageProcessor
{
    private const int MaxAttempts = 10;

    private readonly IWabaRepository _wabas;
    private readonly IPhoneNumberRepository _phoneNumbers;
    private readonly IMessageDefinitionRepository _definitions;
    private readonly IMessageRepository _messages;
    private readonly IRenderedMediaRepository _media;
    private readonly IOperationRepository _operations;
    private readonly IOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMessageMetaClient _messageClient;
    private readonly IMediaMetaClient _mediaClient;
    private readonly IBlobStorage _blobStorage;
    private readonly IContentRendererResolver _rendererResolver;
    private readonly MessageContentStrategyResolver _strategyResolver;
    private readonly TimeProvider _timeProvider;

    public MessageProcessor(
        IWabaRepository wabas,
        IPhoneNumberRepository phoneNumbers,
        IMessageDefinitionRepository definitions,
        IMessageRepository messages,
        IRenderedMediaRepository media,
        IOperationRepository operations,
        IOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        IMessageMetaClient messageClient,
        IMediaMetaClient mediaClient,
        IBlobStorage blobStorage,
        IContentRendererResolver rendererResolver,
        MessageContentStrategyResolver strategyResolver,
        TimeProvider timeProvider)
    {
        _wabas = wabas;
        _phoneNumbers = phoneNumbers;
        _definitions = definitions;
        _messages = messages;
        _media = media;
        _operations = operations;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _messageClient = messageClient;
        _mediaClient = mediaClient;
        _blobStorage = blobStorage;
        _rendererResolver = rendererResolver;
        _strategyResolver = strategyResolver;
        _timeProvider = timeProvider;
    }

    public async Task<MessageProcessingResult> ProcessAsync(
        MessageRequestedEvent requestedEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedEvent);
        var message = await _messages.GetByIdAsync(requestedEvent.MessageId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Message '{requestedEvent.MessageId}' was not found.");
        var operation = await _operations.GetByIdAsync(requestedEvent.OperationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Operation '{requestedEvent.OperationId}' was not found.");
        if (message.Status is WhatsAppMessageStatus.Sent or WhatsAppMessageStatus.Delivered or WhatsAppMessageStatus.Read)
        {
            return new MessageProcessingResult(message.Id, message.Status, message.MetaMessageId, true);
        }

        var waba = await _wabas.GetByIdAsync(message.WabaId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"WABA '{message.WabaId}' was not found.");
        var phone = await _phoneNumbers.GetByIdAsync(message.PhoneNumberId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Phone number '{message.PhoneNumberId}' was not found.");
        var definition = await _definitions.GetAsync(message.MessageType, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Message definition '{message.MessageType}' was not found.");
        var context = new MetaApiContext(waba.CredentialKey);

        operation.Start(_timeProvider.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var renderedMedia = message.ContentStrategy == ContentStrategy.ImageTemplate
                ? await PrepareMediaAsync(message, definition, context, phone, cancellationToken).ConfigureAwait(false)
                : null;

            if (message.Status is WhatsAppMessageStatus.Created or WhatsAppMessageStatus.SendFailed)
            {
                message.TransitionTo(WhatsAppMessageStatus.Queued, _timeProvider.GetUtcNow());
            }
            else if (message.Status == WhatsAppMessageStatus.MediaReady)
            {
                message.TransitionTo(WhatsAppMessageStatus.Queued, _timeProvider.GetUtcNow());
            }

            message.TransitionTo(WhatsAppMessageStatus.Sending, _timeProvider.GetUtcNow());
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var prepared = _strategyResolver.Resolve(message.ContentStrategy)
                .Prepare(definition, message, renderedMedia);
            var sent = await _messageClient.SendAsync(
                    context,
                    new MetaSendMessageRequest(
                        phone.MetaPhoneNumberId,
                        message.Recipient,
                        prepared.Type,
                        prepared.Content),
                    cancellationToken)
                .ConfigureAwait(false);

            var now = _timeProvider.GetUtcNow();
            await _unitOfWork.ExecuteInTransactionAsync(
                _ =>
                {
                    message.MarkSent(sent.MessageId, now);
                    operation.Complete(now);
                    AddStatusEvent(message, now);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
            return new MessageProcessingResult(message.Id, message.Status, message.MetaMessageId, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await HandleFailureAsync(message, operation, exception).ConfigureAwait(false);
        }
    }

    private async Task<RenderedMedia> PrepareMediaAsync(
        WhatsAppMessage message,
        MessageDefinition definition,
        MetaApiContext context,
        PhoneNumber phone,
        CancellationToken cancellationToken)
    {
        var renderedMedia = await _media.GetByMessageIdAsync(message.Id, cancellationToken).ConfigureAwait(false);
        if (renderedMedia is null)
        {
            message.TransitionTo(WhatsAppMessageStatus.Rendering, _timeProvider.GetUtcNow());
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            renderedMedia = await RenderAndStoreAsync(message, definition, cancellationToken).ConfigureAwait(false);
            _media.Add(renderedMedia);
            message.TransitionTo(WhatsAppMessageStatus.Rendered, _timeProvider.GetUtcNow());
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(renderedMedia.MetaMediaId))
        {
            if (message.Status is WhatsAppMessageStatus.Rendered or WhatsAppMessageStatus.MediaUploadFailed)
            {
                message.TransitionTo(WhatsAppMessageStatus.MediaUploading, _timeProvider.GetUtcNow());
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await using var stream = await _blobStorage
                .OpenReadAsync(renderedMedia.StoragePath, cancellationToken)
                .ConfigureAwait(false);
            var upload = await _mediaClient.UploadAsync(
                    context,
                    phone.MetaPhoneNumberId,
                    stream,
                    Path.GetFileName(renderedMedia.StoragePath),
                    renderedMedia.MimeType,
                    cancellationToken)
                .ConfigureAwait(false);
            renderedMedia.SetMetaMediaId(upload.MediaId);
            message.TransitionTo(WhatsAppMessageStatus.MediaReady, _timeProvider.GetUtcNow());
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return renderedMedia;
    }

    private async Task<RenderedMedia> RenderAndStoreAsync(
        WhatsAppMessage message,
        MessageDefinition definition,
        CancellationToken cancellationToken)
    {
        var renderer = _rendererResolver.Resolve(definition.Renderer!);
        var normalizedPayload = JsonCanonicalizer.Canonicalize(message.PayloadJson);
        var hashInput = $"{renderer.Name}\n{renderer.Version}\n{normalizedPayload}";
        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        var now = _timeProvider.GetUtcNow();
        var reusable = await _media
            .GetReusableAsync(sha256, renderer.Name, renderer.Version, now, cancellationToken)
            .ConfigureAwait(false);
        if (reusable is not null)
        {
            return new RenderedMedia(
                Guid.NewGuid(),
                message.Id,
                reusable.StoragePath,
                reusable.MimeType,
                reusable.Sha256,
                reusable.Size,
                reusable.Width,
                reusable.Height,
                reusable.Renderer,
                reusable.RendererVersion,
                now,
                reusable.ExpiresAt);
        }

        var rendered = await renderer.RenderAsync(normalizedPayload, cancellationToken).ConfigureAwait(false);
        await using var content = rendered.Content;
        var extension = string.Equals(rendered.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";
        var path = $"{message.WabaId:D}/{now:yyyy/MM}/{sha256.ToLowerInvariant()}.{extension}";
        var stored = await _blobStorage.UploadAsync(
                path,
                content,
                rendered.MimeType,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["messageId"] = message.Id.ToString("D"),
                    ["renderer"] = renderer.Name,
                    ["rendererVersion"] = renderer.Version,
                    ["sha256"] = sha256
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new RenderedMedia(
            Guid.NewGuid(),
            message.Id,
            stored.Path,
            rendered.MimeType,
            sha256,
            stored.Size,
            rendered.Width,
            rendered.Height,
            renderer.Name,
            renderer.Version,
            now,
            now.AddDays(30));
    }

    private async Task<MessageProcessingResult> HandleFailureAsync(
        WhatsAppMessage message,
        IntegrationOperation operation,
        Exception exception)
    {
        var now = _timeProvider.GetUtcNow();
        var retryable = IsRetryable(exception) && operation.Attempts < MaxAttempts;
        var retryAt = now.Add(GetRetryDelay(operation.Attempts, exception));
        MoveToFailureState(message, retryable, now, exception.Message);
        operation.Fail(exception.Message, retryable ? retryAt : null, !retryable, now);
        await _unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        if (retryable)
        {
            throw new MessageProcessingException(
                $"Message '{message.Id}' failed and can be retried.",
                retryAt,
                exception);
        }

        return new MessageProcessingResult(message.Id, message.Status, message.MetaMessageId, false);
    }

    private void AddStatusEvent(WhatsAppMessage message, DateTimeOffset now)
    {
        var statusEvent = new MessageStatusChangedEvent(
            message.Id,
            message.Status,
            message.MetaMessageId,
            now,
            message.LastError);
        _outbox.Add(new OutboxMessage(
            Guid.NewGuid(),
            nameof(WhatsAppMessage),
            message.Id.ToString("D"),
            IntegrationEventTypes.MessageStatusChanged,
            ApplicationJson.Serialize(statusEvent),
            now,
            message.CorrelationId));
    }

    private static void MoveToFailureState(
        WhatsAppMessage message,
        bool retryable,
        DateTimeOffset now,
        string error)
    {
        var failedState = message.Status switch
        {
            WhatsAppMessageStatus.Rendering => WhatsAppMessageStatus.RenderFailed,
            WhatsAppMessageStatus.MediaUploading => WhatsAppMessageStatus.MediaUploadFailed,
            WhatsAppMessageStatus.Sending => WhatsAppMessageStatus.SendFailed,
            _ => WhatsAppMessageStatus.FailedPermanent
        };
        message.TransitionTo(failedState, now, error);
        if (!retryable && failedState != WhatsAppMessageStatus.FailedPermanent)
        {
            message.TransitionTo(WhatsAppMessageStatus.FailedPermanent, now, error);
        }
    }

    private static bool IsRetryable(Exception exception) => exception switch
    {
        MetaApiException metaException => metaException.Retryable,
        JsonException or ArgumentException or InvalidOperationException => false,
        _ => true
    };

    private static TimeSpan GetRetryDelay(int attempts, Exception exception)
    {
        if (exception is MetaApiException { RetryAfter: { } retryAfter })
        {
            return retryAfter;
        }

        var seconds = Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, attempts - 1)));
        return TimeSpan.FromSeconds(seconds);
    }
}
