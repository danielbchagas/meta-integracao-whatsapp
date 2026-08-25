using Meta.WhatsApp.Infrastructure;
using Meta.WhatsApp.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddWhatsAppInfrastructure(builder.Configuration);
builder.Services.AddScoped<IntegrationEventDispatcher>();
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddHostedService<ServiceBusConsumerWorker>();
builder.Services.AddHostedService<ReconciliationWorker>();

await builder.Build().RunAsync().ConfigureAwait(false);
