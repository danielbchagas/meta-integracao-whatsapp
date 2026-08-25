using System.ComponentModel.DataAnnotations;

namespace Meta.WhatsApp.Infrastructure;

public sealed class MetaGraphOptions
{
    public const string SectionName = "Meta";

    [Required]
    public Uri GraphApiBaseUrl { get; init; } = new("https://graph.facebook.com/");

    [Required]
    [RegularExpression("^v[1-9][0-9]*\\.[0-9]+$")]
    public string ApiVersion { get; init; } = "v23.0";
}

public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    public Uri? VaultUri { get; init; }

    [Range(1, 60)]
    public int SecretCacheMinutes { get; init; } = 5;
}

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string? FullyQualifiedNamespace { get; init; }

    public string TopicName { get; init; } = "whatsapp-events";

    public string SubscriptionName { get; init; } = "whatsapp-worker";

    [Range(1, 100)]
    public int MaxConcurrentCalls { get; init; } = 8;

    [Range(1, 1000)]
    public int MaxDeliveryCount { get; init; } = 10;
}

public sealed class BlobStorageOptions
{
    public const string SectionName = "Storage";

    public Uri? ServiceUri { get; init; }

    public string Container { get; init; } = "whatsapp-media";
}

public sealed class WorkerOptions
{
    public const string SectionName = "Workers";

    [Range(1, 500)]
    public int OutboxBatchSize { get; init; } = 50;

    [Range(1, 300)]
    public int PollIntervalSeconds { get; init; } = 2;

    [Range(10, 3600)]
    public int ClaimTimeoutSeconds { get; init; } = 120;

    [Range(1, 100)]
    public int MaxAttempts { get; init; } = 10;

    [Range(1, 1440)]
    public int ReconciliationIntervalMinutes { get; init; } = 15;
}
