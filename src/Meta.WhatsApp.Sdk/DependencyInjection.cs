using Meta.WhatsApp.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Meta.WhatsApp.Sdk;

public static class DependencyInjection
{
    public static IServiceCollection AddMetaWhatsAppSdk(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddWhatsAppInfrastructure(configuration);
        services.AddScoped<IMetaWhatsAppSdk, MetaWhatsAppSdk>();
        return services;
    }
}
