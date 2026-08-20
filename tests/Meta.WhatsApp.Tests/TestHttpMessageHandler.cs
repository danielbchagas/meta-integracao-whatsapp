using System.Net;
using System.Text;

namespace Meta.WhatsApp.Tests;

internal sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<CapturedRequest> Requests { get; } = [];

    public Func<CapturedRequest, HttpResponseMessage>? Responder { get; set; }

    public Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>>? AsyncResponder { get; set; }

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        lock (_gate)
        {
            _responses.Enqueue(JsonResponse(json, statusCode));
        }
    }

    public static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("Missing request URI."),
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body);

        Func<CapturedRequest, HttpResponseMessage>? responder;
        Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>>? asyncResponder;
        HttpResponseMessage? response = null;
        lock (_gate)
        {
            Requests.Add(captured);
            responder = Responder;
            asyncResponder = AsyncResponder;
            if (responder is null && asyncResponder is null)
            {
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException("No HTTP response was queued for the test.");
                }

                response = _responses.Dequeue();
            }
        }

        if (asyncResponder is not null)
        {
            return await asyncResponder(captured, cancellationToken);
        }

        return responder?.Invoke(captured) ?? response!;
    }
}

internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri Uri,
    string? AuthorizationScheme,
    string? AuthorizationParameter,
    string? Body);

internal sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
