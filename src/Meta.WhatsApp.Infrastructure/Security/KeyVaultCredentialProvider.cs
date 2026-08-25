using System.Collections.Concurrent;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Infrastructure.Security;

public sealed class KeyVaultCredentialProvider : ICredentialProvider
{
    private readonly KeyVaultOptions _options;
    private readonly TokenCredential _tokenCredential;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CachedSecret> _cache = new(StringComparer.Ordinal);
    private readonly object _clientLock = new();
    private SecretClient? _client;

    public KeyVaultCredentialProvider(
        IOptions<KeyVaultOptions> options,
        TokenCredential tokenCredential,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _tokenCredential = tokenCredential;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetAccessTokenAsync(string credentialKey, CancellationToken cancellationToken)
        => await GetSecretAsync(credentialKey, cancellationToken).ConfigureAwait(false);

    public async Task<string> GetSecretAsync(string secretKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new ArgumentException("Secret key is required.", nameof(secretKey));
        }

        var now = _timeProvider.GetUtcNow();
        if (_cache.TryGetValue(secretKey, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Value;
        }

        var response = await GetClient()
            .GetSecretAsync(secretKey, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var value = response.Value.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Key Vault secret '{secretKey}' is empty.");
        }

        _cache[secretKey] = new CachedSecret(
            value,
            now.AddMinutes(_options.SecretCacheMinutes));
        return value;
    }

    private SecretClient GetClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        lock (_clientLock)
        {
            return _client ??= new SecretClient(
                _options.VaultUri ?? throw new InvalidOperationException(
                    "KeyVault:VaultUri must be configured before Meta credentials can be resolved."),
                _tokenCredential);
        }
    }

    private sealed record CachedSecret(string Value, DateTimeOffset ExpiresAt);
}
