using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Meta.WhatsApp.Exceptions;

namespace Meta.WhatsApp.Tests;

public sealed class MetaWhatsAppClientProductionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendTemplateMessageAsync_ExposesRateLimitWithoutAutomaticallyRetryingPost()
    {
        var handler = new TestHttpMessageHandler
        {
            Responder = _ =>
            {
                var response = TestHttpMessageHandler.JsonResponse(
                    "{\"error\":{\"message\":\"Rate limited\",\"code\":4}}",
                    HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                return response;
            }
        };
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MetaWhatsAppApiException>(() =>
            client.SendTemplateMessageAsync("5511999990000", "retomar_atendimento", "pt_BR"));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.True(exception.IsTransient);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task ApiErrors_ClassifyTransientStatusCodes(
        HttpStatusCode statusCode,
        bool expectedTransient)
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson("not-json", statusCode);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MetaWhatsAppApiException>(() =>
            client.SendTemplateMessageAsync("5511999990000", "retomar_atendimento", "pt_BR"));

        Assert.Equal(expectedTransient, exception.IsTransient);
        Assert.Equal("not-json", exception.ResponseBody);
    }

    [Fact]
    public async Task SuccessfulResponseWithInvalidJson_IsMappedToApiException()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson("not-json");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MetaWhatsAppApiException>(() =>
            client.SendTemplateMessageAsync("5511999990000", "retomar_atendimento", "pt_BR"));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.IsType<JsonException>(exception.InnerException);
        Assert.Equal("not-json", exception.ResponseBody);
    }

    [Fact]
    public async Task SuccessfulMessageResponseWithoutIdentifier_IsRejected()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson("{\"messages\":[]}");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MetaWhatsAppApiException>(() =>
            client.SendTemplateMessageAsync("5511999990000", "retomar_atendimento", "pt_BR"));

        Assert.Contains("identifier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendTemplateMessageAsync_PropagatesCancellationToHttpClient()
    {
        var handler = new TestHttpMessageHandler
        {
            AsyncResponder = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable");
            }
        };
        var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SendTemplateMessageAsync(
                "5511999990000",
                "retomar_atendimento",
                "pt_BR",
                cancellationToken: cancellation.Token));
    }

    private static MetaWhatsAppClient CreateClient(TestHttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            new MetaWhatsAppOptions
            {
                AccessToken = "access-token",
                PhoneNumberId = "phone-id",
                BusinessAccountId = "waba-id",
                GraphApiVersion = "v23.0"
            },
            timeProvider: new TestTimeProvider(Now));
}
