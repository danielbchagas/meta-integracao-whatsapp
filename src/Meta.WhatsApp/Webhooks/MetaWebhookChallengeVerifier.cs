using System.Security.Cryptography;
using System.Text;

namespace Meta.WhatsApp.Webhooks;

/// <summary>Validates the GET challenge used when configuring a Meta webhook endpoint.</summary>
public static class MetaWebhookChallengeVerifier
{
    public static bool TryVerify(
        string? mode,
        string? verifyToken,
        string? challenge,
        string expectedVerifyToken,
        out string? verifiedChallenge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedVerifyToken);
        verifiedChallenge = null;
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(verifyToken) ||
            string.IsNullOrWhiteSpace(challenge) ||
            !TokensMatch(verifyToken, expectedVerifyToken))
        {
            return false;
        }

        verifiedChallenge = challenge;
        return true;
    }

    private static bool TokensMatch(string provided, string expected)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
