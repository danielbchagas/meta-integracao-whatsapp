using System.Net;
using System.Text.Json;

namespace Meta.WhatsApp.Application.Abstractions;

public sealed record MetaApiContext(string CredentialKey);

public sealed record MetaWabaSnapshot(string Id, string? BusinessId, string? Name);

public sealed record MetaPhoneNumberSnapshot(
    string Id,
    string DisplayPhoneNumber,
    string? VerifiedName,
    string? QualityRating,
    string? PlatformType,
    string? Status);

public sealed record MetaTemplateSnapshot(
    string Id,
    string Name,
    string Language,
    string Category,
    string Status,
    JsonElement Components);

public sealed record MetaTemplateWriteRequest(
    string Name,
    string Language,
    string Category,
    JsonElement Components);

public sealed record MetaTemplateWriteResult(string Id, string? Status, string? Category);

public sealed record MetaSendMessageRequest(
    string PhoneNumberId,
    string Recipient,
    string Type,
    object Content);

public sealed record MetaSendMessageResult(string MessageId, string? WhatsAppId);

public sealed record MetaMediaUploadResult(string MediaId);

public interface IWabaMetaClient
{
    Task<MetaWabaSnapshot> GetAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken);
}

public interface IPhoneNumberMetaClient
{
    Task<IReadOnlyList<MetaPhoneNumberSnapshot>> GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken);
}

public interface ITemplateMetaClient
{
    Task<IReadOnlyList<MetaTemplateSnapshot>> GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken);

    Task<MetaTemplateWriteResult> CreateAsync(
        MetaApiContext context,
        string wabaId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        MetaApiContext context,
        string templateId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        MetaApiContext context,
        string wabaId,
        string templateName,
        CancellationToken cancellationToken);
}

public interface IMediaMetaClient
{
    Task<MetaMediaUploadResult> UploadAsync(
        MetaApiContext context,
        string phoneNumberId,
        Stream content,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken);
}

public interface IMessageMetaClient
{
    Task<MetaSendMessageResult> SendAsync(
        MetaApiContext context,
        MetaSendMessageRequest request,
        CancellationToken cancellationToken);
}

public sealed class MetaApiException : Exception
{
    public MetaApiException(
        HttpStatusCode statusCode,
        string message,
        int? errorCode = null,
        int? errorSubCode = null,
        string? traceId = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ErrorSubCode = errorSubCode;
        TraceId = traceId;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode StatusCode { get; }

    public int? ErrorCode { get; }

    public int? ErrorSubCode { get; }

    public string? TraceId { get; }

    public TimeSpan? RetryAfter { get; }

    public bool Retryable =>
        StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)StatusCode >= 500;
}
