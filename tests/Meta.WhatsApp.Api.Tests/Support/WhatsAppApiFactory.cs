using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Meta.WhatsApp.Api.Tests.Support;

internal sealed class WhatsAppApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString("N");

    public FakeExternalServices ExternalServices { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<WhatsAppDbContext>().Database.EnsureCreated();
        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<WhatsAppDbContext>>();
            services.RemoveAll<WhatsAppDbContext>();
            foreach (var descriptor in services
                         .Where(item => item.ServiceType.Name.StartsWith(
                             "IDbContextOptionsConfiguration",
                             StringComparison.Ordinal))
                         .ToArray())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<WhatsAppDbContext>(options => options.UseInMemoryDatabase(
                _databaseName,
                database => database.EnableNullChecks(false)));

            services.RemoveAll<IWabaMetaClient>();
            services.RemoveAll<IPhoneNumberMetaClient>();
            services.RemoveAll<ITemplateMetaClient>();
            services.RemoveAll<IMediaMetaClient>();
            services.RemoveAll<IMessageMetaClient>();
            services.RemoveAll<ICredentialProvider>();
            services.RemoveAll<ISecretProvider>();
            services.AddSingleton(ExternalServices);
            services.AddSingleton<IWabaMetaClient>(ExternalServices);
            services.AddSingleton<IPhoneNumberMetaClient>(ExternalServices);
            services.AddSingleton<ITemplateMetaClient>(ExternalServices);
            services.AddSingleton<IMediaMetaClient>(ExternalServices);
            services.AddSingleton<IMessageMetaClient>(ExternalServices);
            services.AddSingleton<ICredentialProvider>(ExternalServices);
            services.AddSingleton<ISecretProvider>(ExternalServices);
        });
    }
}

internal sealed class FakeExternalServices :
    IWabaMetaClient,
    IPhoneNumberMetaClient,
    ITemplateMetaClient,
    IMediaMetaClient,
    IMessageMetaClient,
    ICredentialProvider
{
    public FakeExternalServices()
    {
        using var approved = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Olá {{1}}\"}]");
        using var pending = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Pendente\"}]");
        Templates =
        [
            new MetaTemplateSnapshot(
                "template-approved",
                "proposta_aprovada",
                "pt_BR",
                "UTILITY",
                "APPROVED",
                approved.RootElement.Clone()),
            new MetaTemplateSnapshot(
                "template-pending",
                "ainda_pendente",
                "pt_BR",
                "UTILITY",
                "PENDING",
                pending.RootElement.Clone())
        ];
    }

    public IReadOnlyList<MetaTemplateSnapshot> Templates { get; set; }

    public Task<MetaWabaSnapshot> GetAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MetaWabaSnapshot(wabaId, "business-1", "Empresa Teste"));

    public Task<IReadOnlyList<MetaPhoneNumberSnapshot>> GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetaPhoneNumberSnapshot>>(
        [
            new("phone-1", "+55 11 99999-0000", "Empresa", "GREEN", "CLOUD_API", "CONNECTED")
        ]);

    Task<IReadOnlyList<MetaTemplateSnapshot>> ITemplateMetaClient.GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken) => Task.FromResult(Templates);

    public Task<MetaTemplateWriteResult> CreateAsync(
        MetaApiContext context,
        string wabaId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MetaTemplateWriteResult("created", "PENDING", request.Category));

    public Task UpdateAsync(
        MetaApiContext context,
        string templateId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteAsync(
        MetaApiContext context,
        string wabaId,
        string templateName,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<MetaMediaUploadResult> UploadAsync(
        MetaApiContext context,
        string phoneNumberId,
        Stream content,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MetaMediaUploadResult("media-1"));

    public Task<MetaSendMessageResult> SendAsync(
        MetaApiContext context,
        MetaSendMessageRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new MetaSendMessageResult("wamid-1", request.Recipient));

    public Task<string> GetAccessTokenAsync(string credentialKey, CancellationToken cancellationToken) =>
        Task.FromResult("access-token");

    public Task<string> GetSecretAsync(string secretKey, CancellationToken cancellationToken) =>
        Task.FromResult(secretKey.Contains("verify", StringComparison.OrdinalIgnoreCase)
            ? "verify-token"
            : "app-secret");
}
