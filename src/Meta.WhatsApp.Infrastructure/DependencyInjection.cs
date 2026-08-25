using Azure.Core;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Queries;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Application.Webhooks;
using Meta.WhatsApp.Infrastructure.Messaging;
using Meta.WhatsApp.Infrastructure.Meta;
using Meta.WhatsApp.Infrastructure.Persistence;
using Meta.WhatsApp.Infrastructure.Rendering;
using Meta.WhatsApp.Infrastructure.Security;
using Meta.WhatsApp.Infrastructure.Storage;

namespace Meta.WhatsApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWhatsAppInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<MetaGraphOptions>()
            .Bind(configuration.GetSection(MetaGraphOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KeyVaultOptions>()
            .Bind(configuration.GetSection(KeyVaultOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<BlobStorageOptions>()
            .Bind(configuration.GetSection(BlobStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TokenCredential, DefaultAzureCredential>();

        var connectionString = configuration.GetConnectionString("WhatsApp")
            ?? throw new InvalidOperationException("ConnectionStrings:WhatsApp must be configured.");
        services.AddDbContext<WhatsAppDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(15), errorNumbersToAdd: null)));

        services.AddScoped<EfRepositories>();
        services.AddScoped<IWabaRepository>(ResolveRepositories);
        services.AddScoped<IPhoneNumberRepository>(ResolveRepositories);
        services.AddScoped<ITemplateRepository>(ResolveRepositories);
        services.AddScoped<IMessageDefinitionRepository>(ResolveRepositories);
        services.AddScoped<IMessageRepository>(ResolveRepositories);
        services.AddScoped<IRenderedMediaRepository>(ResolveRepositories);
        services.AddScoped<IOperationRepository>(ResolveRepositories);
        services.AddScoped<IOutboxRepository>(ResolveRepositories);
        services.AddScoped<IInboxRepository>(ResolveRepositories);
        services.AddScoped<IWebhookEventStore>(ResolveRepositories);
        services.AddScoped<IUnitOfWork>(ResolveRepositories);

        services.AddSingleton<ICredentialProvider, KeyVaultCredentialProvider>();
        services.AddSingleton<ISecretProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ICredentialProvider>());
        services.AddSingleton<IBlobStorage, AzureBlobStorage>();
        services.AddSingleton<IIntegrationEventPublisher, AzureServiceBusEventPublisher>();
        services.AddSingleton<IContentRenderer, ProposalSummaryRenderer>();
        services.AddSingleton<IContentRenderer, VehicleSummaryRenderer>();
        services.AddSingleton<IContentRendererResolver, ContentRendererResolver>();

        services.AddSingleton<IMessageContentStrategy, FreeTextContentStrategy>();
        services.AddSingleton<IMessageContentStrategy, TextTemplateContentStrategy>();
        services.AddSingleton<IMessageContentStrategy, ImageTemplateContentStrategy>();
        services.AddSingleton<MessageContentStrategyResolver>();

        services.AddHttpClient(
                MetaGraphApiClient.HttpClientName,
                static (serviceProvider, client) =>
                {
                    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MetaGraphOptions>>().Value;
                    client.BaseAddress = options.GraphApiBaseUrl;
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.ShouldRetryAfterHeader = true;
                options.Retry.DisableForUnsafeHttpMethods();
                options.CircuitBreaker.FailureRatio = 0.2;
                options.CircuitBreaker.MinimumThroughput = 10;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                options.RateLimiter.DefaultRateLimiterOptions.PermitLimit = 20;
                options.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 100;
            });

        services.AddScoped<MetaGraphApiClient>();
        services.AddScoped<IWabaMetaClient>(ResolveMetaClient);
        services.AddScoped<IPhoneNumberMetaClient>(ResolveMetaClient);
        services.AddScoped<ITemplateMetaClient>(ResolveMetaClient);
        services.AddScoped<IMediaMetaClient>(ResolveMetaClient);
        services.AddScoped<IMessageMetaClient>(ResolveMetaClient);

        services.AddScoped<RegisterWabaHandler>();
        services.AddScoped<TemplateManagementService>();
        services.AddScoped<SendNotificationHandler>();
        services.AddScoped<ManagementQueries>();
        services.AddScoped<WebhookIngestionService>();
        services.AddScoped<WebhookEventProcessor>();
        services.AddScoped<MessageProcessor>();
        services.AddScoped<TemplateMutationProcessor>();
        return services;
    }

    private static EfRepositories ResolveRepositories(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<EfRepositories>();

    private static MetaGraphApiClient ResolveMetaClient(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<MetaGraphApiClient>();
}
