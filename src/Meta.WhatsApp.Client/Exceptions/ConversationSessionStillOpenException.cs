namespace Meta.WhatsApp.Exceptions;

public sealed class ConversationSessionStillOpenException : InvalidOperationException
{
    public ConversationSessionStillOpenException(string channelId, string recipient, DateTimeOffset expiresAtUtc)
        : base($"The customer-service session for recipient '{recipient}' on channel '{channelId}' is still open until {expiresAtUtc:O}.")
    {
        ChannelId = channelId;
        Recipient = recipient;
        ExpiresAtUtc = expiresAtUtc;
    }

    public string ChannelId { get; }

    public string Recipient { get; }

    public DateTimeOffset ExpiresAtUtc { get; }
}
