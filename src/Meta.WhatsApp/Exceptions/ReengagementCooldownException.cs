namespace Meta.WhatsApp.Exceptions;

public sealed class ReengagementCooldownException : InvalidOperationException
{
    public ReengagementCooldownException(string recipient, DateTimeOffset retryAtUtc)
        : base($"Reengagement cooldown is active for recipient '{recipient}'. Try again at {retryAtUtc:O}.")
    {
        Recipient = recipient;
        RetryAtUtc = retryAtUtc;
    }

    public string Recipient { get; }

    public DateTimeOffset RetryAtUtc { get; }
}
