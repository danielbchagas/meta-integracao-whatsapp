namespace Meta.WhatsApp.Sessions;

/// <summary>An inbound customer message used to open or renew the local service session.</summary>
public sealed record InboundMessage(
    string Recipient,
    string MessageId,
    DateTimeOffset? ReceivedAtUtc = null,
    string? ContextMessageId = null);
