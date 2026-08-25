namespace Meta.WhatsApp.Exceptions;

/// <summary>Raised when a free-form message is attempted outside the configured customer-service window.</summary>
public sealed class ConversationSessionClosedException : InvalidOperationException
{
    public ConversationSessionClosedException(string recipient)
        : base($"There is no open customer-service session for recipient '{recipient}'. Send an approved template or register a new inbound customer message first.")
    {
        Recipient = recipient;
    }

    public string Recipient { get; }
}
