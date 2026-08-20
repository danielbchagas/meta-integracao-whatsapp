namespace Meta.WhatsApp.Messages;

/// <summary>A message addressed to an individual WhatsApp recipient.</summary>
public sealed record OutboundMessage(
    string Recipient,
    MessageContent Content,
    string? ReplyToMessageId = null,
    bool UseOpenSessionContext = true);

public sealed record SendMessageResult(
    string MessageId,
    string? WhatsAppId,
    string? RecipientInput);
