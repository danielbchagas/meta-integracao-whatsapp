namespace Meta.WhatsApp.Domain;

public sealed class WhatsAppMessage
{
    private static readonly Dictionary<WhatsAppMessageStatus, HashSet<WhatsAppMessageStatus>> Transitions =
        new Dictionary<WhatsAppMessageStatus, HashSet<WhatsAppMessageStatus>>
        {
            [WhatsAppMessageStatus.Created] =
                [WhatsAppMessageStatus.Rendering, WhatsAppMessageStatus.Queued, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.Rendering] =
                [WhatsAppMessageStatus.Rendered, WhatsAppMessageStatus.RenderFailed],
            [WhatsAppMessageStatus.Rendered] =
                [WhatsAppMessageStatus.MediaUploading, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.MediaUploading] =
                [WhatsAppMessageStatus.MediaReady, WhatsAppMessageStatus.MediaUploadFailed],
            [WhatsAppMessageStatus.MediaReady] =
                [WhatsAppMessageStatus.Queued, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.Queued] =
                [WhatsAppMessageStatus.Sending, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.Sending] =
                [WhatsAppMessageStatus.Sent, WhatsAppMessageStatus.SendFailed, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.SendFailed] =
                [WhatsAppMessageStatus.Queued, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.RenderFailed] =
                [WhatsAppMessageStatus.Rendering, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.MediaUploadFailed] =
                [WhatsAppMessageStatus.MediaUploading, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.Sent] =
                [WhatsAppMessageStatus.Delivered, WhatsAppMessageStatus.Read, WhatsAppMessageStatus.FailedPermanent],
            [WhatsAppMessageStatus.Delivered] = [WhatsAppMessageStatus.Read],
            [WhatsAppMessageStatus.Read] = [],
            [WhatsAppMessageStatus.FailedPermanent] = []
        };

    private WhatsAppMessage()
    {
    }

    public WhatsAppMessage(
        Guid id,
        string idempotencyKey,
        Guid wabaId,
        Guid phoneNumberId,
        string recipient,
        string messageType,
        ContentStrategy contentStrategy,
        string payloadJson,
        string? templateName,
        DateTimeOffset now,
        Guid correlationId)
    {
        Id = id;
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey));
        WabaId = wabaId;
        PhoneNumberId = phoneNumberId;
        Recipient = NormalizeRecipient(recipient);
        MessageType = Required(messageType, nameof(messageType)).ToUpperInvariant();
        ContentStrategy = contentStrategy;
        PayloadJson = Required(payloadJson, nameof(payloadJson));
        TemplateName = Trim(templateName);
        Status = WhatsAppMessageStatus.Created;
        CorrelationId = correlationId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string IdempotencyKey { get; private set; } = null!;

    public Guid WabaId { get; private set; }

    public Guid PhoneNumberId { get; private set; }

    public string Recipient { get; private set; } = null!;

    public string MessageType { get; private set; } = null!;

    public ContentStrategy ContentStrategy { get; private set; }

    public string PayloadJson { get; private set; } = null!;

    public string? TemplateName { get; private set; }

    public string? MetaMessageId { get; private set; }

    public WhatsAppMessageStatus Status { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public string? LastError { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void TransitionTo(WhatsAppMessageStatus next, DateTimeOffset now, string? error = null)
    {
        if (!Transitions[Status].Contains(next))
        {
            throw new InvalidOperationException($"Cannot transition a WhatsApp message from {Status} to {next}.");
        }

        Status = next;
        UpdatedAt = now;
        LastError = Trim(error);
    }

    public void MarkSent(string metaMessageId, DateTimeOffset now)
    {
        TransitionTo(WhatsAppMessageStatus.Sent, now);
        MetaMessageId = Required(metaMessageId, nameof(metaMessageId));
        SentAt = now;
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        if (Status == WhatsAppMessageStatus.Sent)
        {
            TransitionTo(WhatsAppMessageStatus.Delivered, now);
        }

        if (DeliveredAt is null || now > DeliveredAt)
        {
            DeliveredAt = now;
        }
    }

    public void MarkRead(DateTimeOffset now)
    {
        if (Status is WhatsAppMessageStatus.Sent or WhatsAppMessageStatus.Delivered)
        {
            TransitionTo(WhatsAppMessageStatus.Read, now);
        }

        DeliveredAt ??= now;
        if (ReadAt is null || now > ReadAt)
        {
            ReadAt = now;
        }
    }

    private static string NormalizeRecipient(string recipient)
    {
        var digits = new string(Required(recipient, nameof(recipient)).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < 8 or > 15)
        {
            throw new ArgumentException("Recipient must contain between 8 and 15 digits.", nameof(recipient));
        }

        return digits;
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
