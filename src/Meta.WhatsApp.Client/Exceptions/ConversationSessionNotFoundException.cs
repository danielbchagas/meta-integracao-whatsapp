namespace Meta.WhatsApp.Exceptions;

public sealed class ConversationSessionNotFoundException : InvalidOperationException
{
    public ConversationSessionNotFoundException(string channelId, string recipient)
        : base($"No previous session exists for recipient '{recipient}' on channel '{channelId}'.")
    {
        ChannelId = channelId;
        Recipient = recipient;
    }

    public string ChannelId { get; }

    public string Recipient { get; }
}
