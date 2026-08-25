using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application;
using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Infrastructure.Meta;

public sealed class MetaGraphApiClient :
    IWabaMetaClient,
    IPhoneNumberMetaClient,
    ITemplateMetaClient,
    IMediaMetaClient,
    IMessageMetaClient
{
    public const string HttpClientName = "MetaGraphApi";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialProvider _credentialProvider;
    private readonly MetaGraphOptions _options;
    private readonly TimeProvider _timeProvider;

    public MetaGraphApiClient(
        IHttpClientFactory httpClientFactory,
        ICredentialProvider credentialProvider,
        IOptions<MetaGraphOptions> options,
        TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _credentialProvider = credentialProvider;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    async Task<MetaWabaSnapshot> IWabaMetaClient.GetAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync<WabaWire>(
                context,
                HttpMethod.Get,
                $"{Path(wabaId)}?fields=id,name,owner_business_info",
                body: null,
                cancellationToken)
            .ConfigureAwait(false);
        return new MetaWabaSnapshot(response.Id, response.OwnerBusinessInfo?.Id, response.Name);
    }

    Task<IReadOnlyList<MetaPhoneNumberSnapshot>> IPhoneNumberMetaClient.GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken) =>
        GetAllPagesAsync<PhoneNumberWire, MetaPhoneNumberSnapshot>(
            context,
            $"{Path(wabaId)}/phone_numbers?fields=id,display_phone_number,verified_name,quality_rating,platform_type,status&limit=100",
            static item => new MetaPhoneNumberSnapshot(
                item.Id,
                item.DisplayPhoneNumber,
                item.VerifiedName,
                item.QualityRating,
                item.PlatformType,
                item.Status),
            cancellationToken);

    Task<IReadOnlyList<MetaTemplateSnapshot>> ITemplateMetaClient.GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken) =>
        GetAllPagesAsync<TemplateWire, MetaTemplateSnapshot>(
            context,
            $"{Path(wabaId)}/message_templates?fields=id,name,language,category,status,components&limit=100",
            static item => new MetaTemplateSnapshot(
                item.Id,
                item.Name,
                item.Language,
                item.Category,
                item.Status,
                item.Components),
            cancellationToken);

    async Task<MetaTemplateWriteResult> ITemplateMetaClient.CreateAsync(
        MetaApiContext context,
        string wabaId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await SendJsonAsync<TemplateWriteWire>(
                context,
                HttpMethod.Post,
                $"{Path(wabaId)}/message_templates",
                new
                {
                    request.Name,
                    request.Language,
                    category = request.Category.ToUpperInvariant(),
                    request.Components
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new MetaTemplateWriteResult(response.Id, response.Status, response.Category);
    }

    async Task ITemplateMetaClient.UpdateAsync(
        MetaApiContext context,
        string templateId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await SendJsonAsync<SuccessWire>(
                context,
                HttpMethod.Post,
                Path(templateId),
                new
                {
                    request.Name,
                    request.Language,
                    category = request.Category.ToUpperInvariant(),
                    request.Components
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw new MetaApiException(HttpStatusCode.OK, "Meta did not confirm the template update.");
        }
    }

    async Task ITemplateMetaClient.DeleteAsync(
        MetaApiContext context,
        string wabaId,
        string templateName,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync<SuccessWire>(
                context,
                HttpMethod.Delete,
                $"{Path(wabaId)}/message_templates?name={Uri.EscapeDataString(templateName)}",
                body: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.Success)
        {
            throw new MetaApiException(HttpStatusCode.OK, "Meta did not confirm the template deletion.");
        }
    }

    async Task<MetaMediaUploadResult> IMediaMetaClient.UploadAsync(
        MetaApiContext context,
        string phoneNumberId,
        Stream content,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("whatsapp", Encoding.UTF8), "messaging_product");
        multipart.Add(new StringContent(mimeType, Encoding.UTF8), "type");
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        multipart.Add(streamContent, "file", fileName);

        var response = await SendAsync(
                context,
                HttpMethod.Post,
                $"{Path(phoneNumberId)}/media",
                multipart,
                cancellationToken)
            .ConfigureAwait(false);
        using (response)
        {
            var wire = await DeserializeAsync<MediaUploadWire>(response, cancellationToken).ConfigureAwait(false);
            return new MetaMediaUploadResult(wire.Id);
        }
    }

    async Task<MetaSendMessageResult> IMessageMetaClient.SendAsync(
        MetaApiContext context,
        MetaSendMessageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = request.Recipient,
            ["type"] = request.Type,
            [request.Type] = request.Content
        };
        var response = await SendJsonAsync<MessageResponseWire>(
                context,
                HttpMethod.Post,
                $"{Path(request.PhoneNumberId)}/messages",
                body,
                cancellationToken)
            .ConfigureAwait(false);
        var sent = response.Messages.Count > 0
            ? response.Messages[0]
            : throw new MetaApiException(HttpStatusCode.OK, "Meta returned success without a message ID.");
        var whatsAppId = response.Contacts.Count > 0 ? response.Contacts[0].WhatsAppId : null;
        return new MetaSendMessageResult(sent.Id, whatsAppId);
    }

    private async Task<IReadOnlyList<TResult>> GetAllPagesAsync<TWire, TResult>(
        MetaApiContext context,
        string initialPath,
        Func<TWire, TResult> map,
        CancellationToken cancellationToken)
    {
        var results = new List<TResult>();
        var visitedCursors = new HashSet<string>(StringComparer.Ordinal);
        string? path = initialPath;
        while (path is not null)
        {
            var page = await SendJsonAsync<PageWire<TWire>>(
                    context,
                    HttpMethod.Get,
                    path,
                    body: null,
                    cancellationToken)
                .ConfigureAwait(false);
            results.AddRange(page.Data.Select(map));
            var after = string.IsNullOrWhiteSpace(page.Paging?.Next) ? null : page.Paging?.Cursors?.After;
            path = after is not null && visitedCursors.Add(after)
                ? AddOrReplaceAfter(initialPath, after)
                : null;
        }

        return results;
    }

    private async Task<T> SendJsonAsync<T>(
        MetaApiContext context,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var content = body is null
            ? null
            : new StringContent(JsonSerializer.Serialize(body, body.GetType(), SerializerOptions), Encoding.UTF8, "application/json");
        using var response = await SendAsync(context, method, path, content, cancellationToken).ConfigureAwait(false);
        return await DeserializeAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(
        MetaApiContext context,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var token = await _credentialProvider
            .GetAccessTokenAsync(context.CredentialKey, cancellationToken)
            .ConfigureAwait(false);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            method,
            $"{_options.ApiVersion}/{path.TrimStart('/')}")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new JsonException("Meta response body was empty.");
        }
        catch (JsonException exception)
        {
            throw new MetaApiException(
                response.StatusCode,
                "Meta returned an invalid JSON response.",
                innerException: exception);
        }
    }

    private async Task<MetaApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ErrorEnvelopeWire? envelope = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            envelope = await JsonSerializer
                .DeserializeAsync<ErrorEnvelopeWire>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // The status and reason phrase still provide a safe normalized error.
        }

        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date)
        {
            retryAfter = date - _timeProvider.GetUtcNow();
        }

        if (retryAfter < TimeSpan.Zero)
        {
            retryAfter = TimeSpan.Zero;
        }

        return new MetaApiException(
            response.StatusCode,
            envelope?.Error?.Message ?? string.Create(
                CultureInfo.InvariantCulture,
                $"Meta Graph API returned {(int)response.StatusCode} {response.ReasonPhrase}."),
            envelope?.Error?.Code,
            envelope?.Error?.ErrorSubcode,
            envelope?.Error?.TraceId,
            retryAfter);
    }

    private static string AddOrReplaceAfter(string path, string after)
    {
        var marker = "&after=";
        var markerIndex = path.IndexOf(marker, StringComparison.Ordinal);
        var basePath = markerIndex < 0 ? path : path[..markerIndex];
        return $"{basePath}{marker}{Uri.EscapeDataString(after)}";
    }

    private static string Path(string value) => Uri.EscapeDataString(
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A resource identifier is required.", nameof(value))
            : value);

    private sealed record WabaWire(
        string Id,
        string? Name,
        [property: JsonPropertyName("owner_business_info")] BusinessWire? OwnerBusinessInfo);

    private sealed record BusinessWire(string? Id);

    private sealed record PhoneNumberWire(
        string Id,
        [property: JsonPropertyName("display_phone_number")] string DisplayPhoneNumber,
        [property: JsonPropertyName("verified_name")] string? VerifiedName,
        [property: JsonPropertyName("quality_rating")] string? QualityRating,
        [property: JsonPropertyName("platform_type")] string? PlatformType,
        string? Status);

    private sealed record TemplateWire(
        string Id,
        string Name,
        string Language,
        string Category,
        string Status,
        JsonElement Components);

    private sealed record TemplateWriteWire(string Id, string? Status, string? Category);

    private sealed record SuccessWire(bool Success);

    private sealed record MediaUploadWire(string Id);

    private sealed record PageWire<T>(IReadOnlyList<T> Data, PagingWire? Paging);

    private sealed record PagingWire(CursorWire? Cursors, string? Next);

    private sealed record CursorWire(string? After);

    private sealed record MessageResponseWire(
        IReadOnlyList<ContactWire> Contacts,
        IReadOnlyList<MessageWire> Messages);

    private sealed record ContactWire([property: JsonPropertyName("wa_id")] string? WhatsAppId);

    private sealed record MessageWire(string Id);

    private sealed record ErrorEnvelopeWire(ErrorWire? Error);

    private sealed record ErrorWire(
        string? Message,
        int? Code,
        [property: JsonPropertyName("error_subcode")] int? ErrorSubcode,
        [property: JsonPropertyName("fbtrace_id")] string? TraceId);
}
