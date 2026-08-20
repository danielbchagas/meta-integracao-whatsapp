using System.Net;
using System.Text;

namespace Meta.WhatsApp.Specs.Support;

internal sealed class ScenarioHttpMessageHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<ScenarioHttpRequest> Requests { get; } = [];

    public Func<ScenarioHttpRequest, HttpResponseMessage>? Responder { get; set; }

    public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        Enqueue(() => JsonResponse(json, statusCode));

    public void EnqueueException(Exception exception) =>
        Enqueue(() => throw exception);

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
        var captured = new ScenarioHttpRequest(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("Request URI is required."),
            body);

        Func<ScenarioHttpRequest, HttpResponseMessage>? responder;
        Func<HttpResponseMessage>? queued = null;
        lock (_gate)
        {
            Requests.Add(captured);
            responder = Responder;
            if (responder is null)
            {
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException($"No response was configured for {request.Method} {request.RequestUri}.");
                }

                queued = _responses.Dequeue();
            }
        }

        return responder?.Invoke(captured) ?? queued!();
    }

    private void Enqueue(Func<HttpResponseMessage> response)
    {
        lock (_gate)
        {
            _responses.Enqueue(response);
        }
    }
}

internal sealed record ScenarioHttpRequest(HttpMethod Method, Uri Uri, string? Body);
