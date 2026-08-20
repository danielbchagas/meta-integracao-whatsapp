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
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ErrorSubcode = errorSubcode;
        ErrorType = errorType;
        TraceId = traceId;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public int? ErrorCode { get; }

    public int? ErrorSubcode { get; }

    public string? ErrorType { get; }

    public string? TraceId { get; }

    public string? ResponseBody { get; }
}
