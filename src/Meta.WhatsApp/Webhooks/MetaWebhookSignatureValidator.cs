using System.Security.Cryptography;
using System.Text;
using Meta.WhatsApp.Exceptions;

namespace Meta.WhatsApp.Webhooks;

/// <summary>Validates the <c>X-Hub-Signature-256</c> header using the Meta app secret.</summary>
public static class MetaWebhookSignatureValidator
{
    private const string Prefix = "sha256=";

    public static bool IsValid(
        ReadOnlySpan<byte> payload,
        string? signatureHeader,
        string appSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appSecret);
        if (string.IsNullOrWhiteSpace(signatureHeader) ||
            !signatureHeader.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] receivedSignature;
        try
        {
            receivedSignature = Convert.FromHexString(signatureHeader[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), payload);
        return receivedSignature.Length == expectedSignature.Length &&
            CryptographicOperations.FixedTimeEquals(receivedSignature, expectedSignature);
    }

    public static void EnsureValid(
        ReadOnlySpan<byte> payload,
        string? signatureHeader,
        string appSecret)
    {
        if (!IsValid(payload, signatureHeader, appSecret))
        {
            throw new MetaWebhookException("The Meta webhook signature is invalid.");
        }
    }
}
