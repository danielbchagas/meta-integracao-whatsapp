using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Meta.WhatsApp.Api;
using Meta.WhatsApp.Sdk;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOptions<WebhookEndpointOptions>()
    .Bind(builder.Configuration.GetSection(WebhookEndpointOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddMetaWhatsAppSdk(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("azure-sql", tags: ["ready"]);

var app = builder.Build();
app.UseExceptionHandler();
app.MapWhatsAppEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

await app.RunAsync().ConfigureAwait(false);

public partial class Program;
