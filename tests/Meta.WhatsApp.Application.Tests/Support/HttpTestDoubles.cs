using System.Net;
using System.Text;
using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Application.Tests.Support;

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body,
            request.Content?.Headers.ContentType?.MediaType));
        return _responses.Count > 0
            ? _responses.Dequeue()
            : throw new InvalidOperationException("No HTTP response was queued.");
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? AuthenticationScheme,
    string? AuthenticationToken,
    string? Body,
    string? ContentType);

internal sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

internal sealed class FixedSecretProvider(string token) : ICredentialProvider
{
    public Task<string> GetAccessTokenAsync(string credentialKey, CancellationToken cancellationToken) =>
        GetSecretAsync(credentialKey, cancellationToken);

    public Task<string> GetSecretAsync(string secretKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(token);
    }
}
