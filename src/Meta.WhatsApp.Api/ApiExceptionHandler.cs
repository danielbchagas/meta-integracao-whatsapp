using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Exceptions;

namespace Meta.WhatsApp.Api;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, int, string, Exception?> LogRequestFailed =
        LoggerMessage.Define<int, string>(
            LogLevel.Error,
            new EventId(1001, nameof(ApiExceptionHandler)),
            "Request failed with status {StatusCode}; trace {TraceId}");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var (status, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            MetaWebhookException => (StatusCodes.Status400BadRequest, "Invalid webhook"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Operation conflict"),
            MetaApiException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } =>
                (StatusCodes.Status502BadGateway, "Meta credential was rejected"),
            MetaApiException => (StatusCodes.Status502BadGateway, "Meta Graph API failure"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        LogRequestFailed(logger, status, httpContext.TraceIdentifier, exception);
        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status < 500 ? exception.Message : null,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            },
            Exception = exception
        }).ConfigureAwait(false);
    }
}
