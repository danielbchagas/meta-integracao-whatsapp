using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Infrastructure.Meta;
using Microsoft.Extensions.Options;

namespace Meta.WhatsApp.Infrastructure.Tests;

public sealed class MetaGraphApiClientTests
{
    [Fact]
    public async Task WabaAndPhoneContractsAreMappedAndAuthorized()
    {
        var handler = new RecordingHandler();
        handler.EnqueueJson("{\"id\":\"waba-1\",\"name\":\"Empresa\",\"owner_business_info\":{\"id\":\"business-1\"}}");
        handler.EnqueueJson("{\"data\":[{\"id\":\"phone-1\",\"display_phone_number\":\"+55 11\",\"verified_name\":\"Empresa\",\"quality_rating\":\"GREEN\",\"platform_type\":\"CLOUD_API\",\"status\":\"CONNECTED\"}]}");
        var client = CreateClient(handler);

        var waba = await ((IWabaMetaClient)client).GetAsync(
            new MetaApiContext("credential"),
            "waba-1",
            CancellationToken.None);
        var phones = await ((IPhoneNumberMetaClient)client).GetAllAsync(
            new MetaApiContext("credential"),
            "waba-1",
            CancellationToken.None);

        Assert.Equal("business-1", waba.BusinessId);
        Assert.Equal("phone-1", Assert.Single(phones).Id);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("Bearer", request.AuthenticationScheme);
            Assert.Equal("secret-token", request.AuthenticationToken);
        });
    }

    [Fact]
    public async Task TemplatePaginationUsesEveryCursorOnce()
    {
        var handler = new RecordingHandler();
        handler.EnqueueJson(
            "{\"data\":[{\"id\":\"t1\",\"name\":\"a\",\"language\":\"pt_BR\",\"category\":\"UTILITY\",\"status\":\"APPROVED\",\"components\":[]}],\"paging\":{\"cursors\":{\"after\":\"cursor 2\"},\"next\":\"https://next\"}}");
        handler.EnqueueJson(
            "{\"data\":[{\"id\":\"t2\",\"name\":\"b\",\"language\":\"en_US\",\"category\":\"MARKETING\",\"status\":\"PENDING\",\"components\":[]}]}");
        var client = CreateClient(handler);

        var templates = await ((ITemplateMetaClient)client).GetAllAsync(
            new MetaApiContext("credential"),
            "waba-1",
            CancellationToken.None);

        Assert.Equal(2, templates.Count);
        Assert.Contains("after=cursor%202", handler.Requests[1].Uri.Query, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RepeatedPaginationCursorStopsWithoutLooping()
    {
        var handler = new RecordingHandler();
        var page = "{\"data\":[],\"paging\":{\"cursors\":{\"after\":\"same\"},\"next\":\"https://next\"}}";
        handler.EnqueueJson(page);
        handler.EnqueueJson(page);
        var client = CreateClient(handler);

        var templates = await ((ITemplateMetaClient)client).GetAllAsync(
            new MetaApiContext("credential"),
            "waba-1",
            CancellationToken.None);

        Assert.Empty(templates);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TemplateCreateUpdateAndDeleteUseExpectedPayloads()
    {
        var handler = new RecordingHandler();
        handler.EnqueueJson("{\"id\":\"created\",\"status\":\"PENDING\",\"category\":\"UTILITY\"}");
        handler.EnqueueJson("{\"success\":true}");
        handler.EnqueueJson("{\"success\":true}");
        var client = CreateClient(handler);
        using var components = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Olá {{1}}\"}]");
        var request = new MetaTemplateWriteRequest(
            "pedido_aprovado",
            "pt_BR",
            "utility",
            components.RootElement.Clone());

        var created = await ((ITemplateMetaClient)client).CreateAsync(
            new MetaApiContext("credential"),
            "waba-1",
            request,
            CancellationToken.None);
        await ((ITemplateMetaClient)client).UpdateAsync(
            new MetaApiContext("credential"),
            "template-1",
            request,
            CancellationToken.None);
        await ((ITemplateMetaClient)client).DeleteAsync(
            new MetaApiContext("credential"),
            "waba-1",
            "pedido aprovado",
            CancellationToken.None);

        Assert.Equal("created", created.Id);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("\"category\":\"UTILITY\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.Contains("name=pedido%20aprovado", handler.Requests[2].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MediaUploadUsesMultipartAndReturnsMediaId()
    {
        var handler = new RecordingHandler();
        handler.EnqueueJson("{\"id\":\"media-1\"}");
        var client = CreateClient(handler);
        await using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await ((IMediaMetaClient)client).UploadAsync(
            new MetaApiContext("credential"),
            "phone-1",
            content,
            "summary.png",
            "image/png",
            CancellationToken.None);

        Assert.Equal("media-1", result.MediaId);
        Assert.Equal("multipart/form-data", handler.Requests[0].ContentType);
        Assert.Contains("summary.png", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("image/png", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MessageSendUsesWhatsAppEnvelopeAndMapsIdentifiers()
    {
        var handler = new RecordingHandler();
        handler.EnqueueJson("{\"contacts\":[{\"wa_id\":\"5511999990000\"}],\"messages\":[{\"id\":\"wamid.1\"}]}");
        var client = CreateClient(handler);

        var result = await ((IMessageMetaClient)client).SendAsync(
            new MetaApiContext("credential"),
            new MetaSendMessageRequest(
                "phone-1",
                "5511999990000",
                "text",
                new { body = "Olá" }),
            CancellationToken.None);

        Assert.Equal("wamid.1", result.MessageId);
        Assert.Equal("5511999990000", result.WhatsAppId);
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("whatsapp", body.RootElement.GetProperty("messaging_product").GetString());
        Assert.Equal("text", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("Olá", body.RootElement.GetProperty("text").GetProperty("body").GetString());
    }

    [Fact]
    public async Task StructuredMetaErrorPreservesRetryClassificationAndDetails()
    {
        var handler = new RecordingHandler();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                "{\"error\":{\"message\":\"Rate limited\",\"code\":4,\"error_subcode\":2446079,\"fbtrace_id\":\"trace-1\"}}",
                Encoding.UTF8,
                "application/json")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
        handler.Enqueue(response);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MetaApiException>(() =>
            ((IWabaMetaClient)client).GetAsync(
                new MetaApiContext("credential"),
                "waba-1",
                CancellationToken.None));

        Assert.True(exception.Retryable);
        Assert.Equal(4, exception.ErrorCode);
        Assert.Equal(2446079, exception.ErrorSubCode);
        Assert.Equal("trace-1", exception.TraceId);
        Assert.Equal(TimeSpan.FromSeconds(45), exception.RetryAfter);
    }

    [Fact]
    public async Task InvalidSuccessfulJsonIsNormalizedAsMetaFailure()
    {
        var handler = new RecordingHandler();
        handler.EnqueueJson("not-json");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MetaApiException>(() =>
            ((IWabaMetaClient)client).GetAsync(
                new MetaApiContext("credential"),
                "waba-1",
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Contains("invalid JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(exception.Retryable);
    }

    private static MetaGraphApiClient CreateClient(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.facebook.com/")
        };
        return new MetaGraphApiClient(
            new SingleClientFactory(httpClient),
            new FixedCredentialProvider(),
            Options.Create(new MetaGraphOptions
            {
                GraphApiBaseUrl = httpClient.BaseAddress,
                ApiVersion = "v23.0"
            }),
            TimeProvider.System);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void EnqueueJson(string json) => Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthenticationScheme,
        string? AuthenticationToken,
        string? ContentType,
        string? Body);

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedCredentialProvider : ICredentialProvider
    {
        public Task<string> GetAccessTokenAsync(string credentialKey, CancellationToken cancellationToken) =>
            Task.FromResult("secret-token");

        public Task<string> GetSecretAsync(string secretKey, CancellationToken cancellationToken) =>
            Task.FromResult("secret-token");
    }
}
