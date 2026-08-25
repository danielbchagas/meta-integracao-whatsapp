using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Meta.WhatsApp.Infrastructure.Persistence;

namespace Meta.WhatsApp.Api;

public sealed class DatabaseHealthCheck(WhatsAppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("Azure SQL is reachable.")
                : HealthCheckResult.Unhealthy("Azure SQL is not reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Azure SQL health check failed.", exception);
        }
    }
}
