using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Domain;
using Meta.WhatsApp.Infrastructure;

namespace Meta.WhatsApp.Worker;

public sealed class ReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<ReconciliationWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, Exception?> LogReconciliationFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2201, nameof(ReconciliationWorker)),
            "WABA reconciliation failed for {MetaWabaId}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(options.Value.ReconciliationIntervalMinutes),
            timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ReconcileAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWabaRepository>();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterWabaHandler>();
        var wabas = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var waba in wabas.Where(item => item.Status == WabaStatus.Active))
        {
            try
            {
                await handler.HandleAsync(
                        new RegisterWabaCommand(waba.MetaWabaId, waba.CredentialKey),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReconciliationFailure(logger, waba.MetaWabaId, exception);
            }
        }
    }
}
