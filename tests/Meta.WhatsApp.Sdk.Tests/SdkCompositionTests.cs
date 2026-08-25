using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Queries;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meta.WhatsApp.Sdk.Tests;

public sealed class SdkCompositionTests
{
    [Fact]
    public void SdkAssemblyReferencesClientApplicationAndInfrastructure()
    {
        var references = typeof(IMetaWhatsAppSdk).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Meta.WhatsApp.Client", references);
        Assert.Contains("Meta.WhatsApp.Application", references);
        Assert.Contains("Meta.WhatsApp.Infrastructure", references);
    }

    [Fact]
    public void RegistrationExposesFacadeUseCasesAndInfrastructureServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WhatsApp"] =
                    "Server=(localdb)\\mssqllocaldb;Database=WhatsAppSdkTests;Trusted_Connection=True",
                ["Meta:GraphApiBaseUrl"] = "https://graph.facebook.com/",
                ["Meta:ApiVersion"] = "v23.0"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddMetaWhatsAppSdk(configuration);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMetaWhatsAppSdk));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(RegisterWabaHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TemplateManagementService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(SendNotificationHandler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ManagementQueries));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWabaRepository));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ITemplateMetaClient));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WhatsAppDbContext));
    }
}
