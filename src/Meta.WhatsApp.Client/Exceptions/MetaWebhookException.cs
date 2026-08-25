namespace Meta.WhatsApp.Exceptions;

/// <summary>Represents an invalid or unauthenticated webhook received from Meta.</summary>
public sealed class MetaWebhookException : Exception
{
    public MetaWebhookException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
