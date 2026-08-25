using System.Net;

namespace Meta.WhatsApp.Exceptions;

/// <summary>Represents a non-success response returned by the Meta Graph API.</summary>
public sealed class MetaWhatsAppApiException : Exception
{
    public MetaWhatsAppApiException(
        HttpStatusCode statusCode,
        string message,
        int? errorCode = null,
        int? errorSubcode = null,
        string? errorType = null,
        string? traceId = null,
        string? responseBody = null,
        Exception? innerException = null,
        TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ErrorSubcode = errorSubcode;
        ErrorType = errorType;
        TraceId = traceId;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public int? ErrorCode { get; }

    public int? ErrorSubcode { get; }

    public string? ErrorType { get; }

    public string? TraceId { get; }

    public string? ResponseBody { get; }

    /// <summary>Delay requested by Meta through the Retry-After response header.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Indicates a timeout, rate limit, or server-side failure. Retrying message sends still requires
    /// application-level idempotency because delivery may have occurred before the failure was observed.
    /// </summary>
    public bool IsTransient =>
        StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)StatusCode >= 500;
}
