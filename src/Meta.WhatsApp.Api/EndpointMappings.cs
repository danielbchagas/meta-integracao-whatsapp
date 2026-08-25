using System.Text.Json;
using Meta.WhatsApp.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Queries;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Application.Webhooks;
using Meta.WhatsApp.Sdk;

namespace Meta.WhatsApp.Api;

public static class EndpointMappings
{
    public static IEndpointRouteBuilder MapWhatsAppEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var api = endpoints.MapGroup("/api");

        api.MapPost("/wabas", RegisterWabaAsync);
        api.MapGet("/wabas/{wabaId}", GetWabaAsync);
        api.MapGet("/wabas/{wabaId}/phone-numbers", GetPhoneNumbersAsync);
        api.MapGet("/wabas/{wabaId}/templates", GetTemplatesAsync);
        api.MapGet("/wabas/{wabaId}/templates/available", GetAvailableTemplatesAsync);
        api.MapGet("/wabas/{wabaId}/templates/{templateId:guid}", GetTemplateAsync);
        api.MapPost("/wabas/{wabaId}/templates", CreateTemplateAsync);
        api.MapPut("/wabas/{wabaId}/templates/{templateId:guid}", UpdateTemplateAsync);
        api.MapDelete("/wabas/{wabaId}/templates/{templateId:guid}", DeleteTemplateAsync);
        api.MapGet("/operations/{operationId:guid}", GetOperationAsync);
        api.MapPost("/messages", SendMessageAsync);
        api.MapGet("/messages/{messageId:guid}", GetMessageAsync);

        endpoints.MapGet("/webhooks/meta", VerifyWebhookAsync);
        endpoints.MapPost("/webhooks/meta", ReceiveWebhookAsync);
        return endpoints;
    }

    private static async Task<IResult> RegisterWabaAsync(
        RegisterWabaRequest request,
        IMetaWhatsAppSdk sdk,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await sdk.RegisterWabaAsync(
                new RegisterWabaCommand(request.WabaId, request.CredentialKey, GetCorrelationId(httpContext)),
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Created($"/api/wabas/{Uri.EscapeDataString(result.MetaWabaId)}", result);
    }

    private static async Task<IResult> GetWabaAsync(
        string wabaId,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken)
    {
        var result = await sdk.GetWabaAsync(wabaId, cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetPhoneNumbersAsync(
        string wabaId,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken) =>
        Results.Ok(await sdk.GetPhoneNumbersAsync(wabaId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetTemplatesAsync(
        string wabaId,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken) =>
        Results.Ok(await sdk.GetTemplatesAsync(wabaId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetAvailableTemplatesAsync(
        string wabaId,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken) =>
        Results.Ok(await sdk.GetAvailableTemplatesAsync(wabaId, cancellationToken).ConfigureAwait(false));

    private static async Task<IResult> GetTemplateAsync(
        string wabaId,
        Guid templateId,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken)
    {
        var result = await sdk.GetTemplateAsync(wabaId, templateId, cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> CreateTemplateAsync(
        string wabaId,
        TemplateWriteRequest request,
        IMetaWhatsAppSdk sdk,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await sdk.QueueTemplateCreateAsync(
                new CreateTemplateCommand(
                    wabaId,
                    request.Name ?? throw new ArgumentException("Name is required."),
                    request.Language ?? throw new ArgumentException("Language is required."),
                    request.Category,
                    request.Components,
                    GetCorrelationId(httpContext)),
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Accepted($"/api/operations/{result.OperationId:D}", result);
    }

    private static async Task<IResult> UpdateTemplateAsync(
        string wabaId,
        Guid templateId,
        TemplateWriteRequest request,
        IMetaWhatsAppSdk sdk,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await sdk.QueueTemplateUpdateAsync(
                new UpdateTemplateCommand(
                    wabaId,
                    templateId,
                    request.Category,
                    request.Components,
                    GetCorrelationId(httpContext)),
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Accepted($"/api/operations/{result.OperationId:D}", result);
    }

    private static async Task<IResult> DeleteTemplateAsync(
        string wabaId,
        Guid templateId,
        IMetaWhatsAppSdk sdk,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await sdk.QueueTemplateDeleteAsync(
                new DeleteTemplateCommand(wabaId, templateId, GetCorrelationId(httpContext)),
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Accepted($"/api/operations/{result.OperationId:D}", result);
    }

    private static async Task<IResult> GetOperationAsync(
        Guid operationId,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken)
    {
        var result = await sdk.GetOperationAsync(operationId, cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> SendMessageAsync(
        SendNotificationRequest request,
        IMetaWhatsAppSdk sdk,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Idempotency-Key header is required.");
        }

        var result = await sdk.SendNotificationAsync(
                new SendNotificationCommand(
                    request.WabaId,
                    request.PhoneNumberId,
                    request.MessageType,
                    request.Recipient,
                    request.Data,
                    idempotencyKey,
                    GetCorrelationId(httpContext)),
                cancellationToken)
            .ConfigureAwait(false);
        return Results.Accepted($"/api/messages/{result.MessageId:D}", result);
    }

    private static async Task<IResult> GetMessageAsync(
        Guid messageId,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken)
    {
        var result = await sdk.GetMessageAsync(messageId, cancellationToken).ConfigureAwait(false);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> VerifyWebhookAsync(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        ISecretProvider secrets,
        IOptions<WebhookEndpointOptions> options,
        CancellationToken cancellationToken)
    {
        var expectedToken = await secrets
            .GetSecretAsync(options.Value.VerifyTokenSecretKey, cancellationToken)
            .ConfigureAwait(false);
        return MetaWebhookChallengeVerifier.TryVerify(mode, verifyToken, challenge, expectedToken, out var verified)
            ? Results.Text(verified!, "text/plain")
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ReceiveWebhookAsync(
        HttpContext httpContext,
        ISecretProvider secrets,
        IOptions<WebhookEndpointOptions> options,
        IMetaWhatsAppSdk sdk,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var payload = buffer.ToArray();
        var appSecret = await secrets
            .GetSecretAsync(options.Value.AppSecretSecretKey, cancellationToken)
            .ConfigureAwait(false);
        if (!MetaWebhookSignatureValidator.IsValid(
                payload,
                httpContext.Request.Headers["X-Hub-Signature-256"].FirstOrDefault(),
                appSecret))
        {
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var result = await sdk
            .IngestWebhookPayloadAsync(payload, GetCorrelationId(httpContext), cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static Guid GetCorrelationId(HttpContext context) =>
        Guid.TryParse(context.Request.Headers["X-Correlation-Id"].FirstOrDefault(), out var correlationId)
            ? correlationId
            : Guid.NewGuid();
}

public sealed record RegisterWabaRequest(string WabaId, string CredentialKey);

public sealed record TemplateWriteRequest(
    string? Name,
    string? Language,
    string Category,
    JsonElement Components);

public sealed record SendNotificationRequest(
    string WabaId,
    string PhoneNumberId,
    string MessageType,
    string Recipient,
    JsonElement Data);

public sealed class WebhookEndpointOptions
{
    public const string SectionName = "Webhook";

    public string VerifyTokenSecretKey { get; init; } = "meta-webhook-verify-token";

    public string AppSecretSecretKey { get; init; } = "meta-app-secret";
}
