using System.Text.RegularExpressions;

namespace Meta.WhatsApp;

/// <summary>Configuration required by the Meta Graph API.</summary>
public sealed partial class MetaWhatsAppOptions
{
    public required string AccessToken { get; init; }

    public required string PhoneNumberId { get; init; }

    public required string BusinessAccountId { get; init; }

    /// <summary>
    /// Version including the <c>v</c> prefix, for example <c>v23.0</c>.
    /// It is intentionally required so the consuming application controls API upgrades.
    /// </summary>
    public required string GraphApiVersion { get; init; }

    public Uri GraphApiBaseAddress { get; init; } = new("https://graph.facebook.com/");

    public TimeSpan CustomerServiceWindow { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Prevents free-form messages when no inbound customer message has opened the service window.
    /// </summary>
    public bool RequireOpenSessionForFreeFormMessages { get; init; } = true;

    /// <summary>
    /// Adds the latest inbound message ID as reply context while a local session is open.
    /// </summary>
    public bool AttachReplyContextToOpenSession { get; init; } = true;

    /// <summary>Minimum interval between reengagement attempts for the same channel and recipient.</summary>
    public TimeSpan ReengagementCooldown { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum number of reengagement attempts retained in each session.</summary>
    public int MaxReengagementHistory { get; init; } = 20;

    internal void Validate()
    {
        RequireValue(AccessToken, nameof(AccessToken));
        RequireValue(PhoneNumberId, nameof(PhoneNumberId));
        RequireValue(BusinessAccountId, nameof(BusinessAccountId));
        RequireValue(GraphApiVersion, nameof(GraphApiVersion));

        if (!GraphApiVersionRegex().IsMatch(GraphApiVersion))
        {
            throw new ArgumentException(
                "GraphApiVersion must use the format 'v<major>.<minor>', for example 'v23.0'.",
                nameof(GraphApiVersion));
        }

        if (!GraphApiBaseAddress.IsAbsoluteUri || GraphApiBaseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("GraphApiBaseAddress must be an absolute HTTPS URI.", nameof(GraphApiBaseAddress));
        }

        if (CustomerServiceWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(CustomerServiceWindow), "The service window must be positive.");
        }

        if (ReengagementCooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReengagementCooldown), "The cooldown cannot be negative.");
        }

        if (MaxReengagementHistory <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxReengagementHistory), "History size must be positive.");
        }
    }

    private static void RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }
    }

    [GeneratedRegex("^v[1-9][0-9]*\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex GraphApiVersionRegex();
}
