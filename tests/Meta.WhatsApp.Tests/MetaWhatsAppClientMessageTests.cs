using System.Net;
using System.Text.Json;
using Meta.WhatsApp.Exceptions;
using Meta.WhatsApp.Messages;
using Meta.WhatsApp.Sessions;

namespace Meta.WhatsApp.Tests;

public sealed class MetaWhatsAppClientMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendTextMessageAsync_ReusesLatestInboundContextForOpenSession()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(SentMessageResponse("wamid.out.1"));
        handler.EnqueueJson(SentMessageResponse("wamid.out.2"));
        var client = CreateClient(handler);

        await client.RegisterInboundMessageAsync(
            new InboundMessage("+55 (11) 99999-0000", "wamid.inbound"));

        await client.SendTextMessageAsync("5511999990000", "Primeira resposta");
        await client.SendTextMessageAsync("+55 11 99999-0000", "Segunda resposta");

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://graph.facebook.com/v23.0/phone-id/messages", request.Uri.ToString());
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal("access-token", request.AuthorizationParameter);

            using var json = JsonDocument.Parse(request.Body!);
            Assert.Equal("5511999990000", json.RootElement.GetProperty("to").GetString());
            Assert.Equal(
                "wamid.inbound",
                json.RootElement.GetProperty("context").GetProperty("message_id").GetString());
        });
    }

    [Fact]
    public async Task RegisterInboundMessageAsync_RenewsSessionAndReplyContext()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(SentMessageResponse("wamid.out"));
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);

        await client.RegisterInboundMessageAsync(new InboundMessage("5511999990000", "wamid.old"));
        timeProvider.UtcNow = Now.AddHours(23);
        await client.RegisterInboundMessageAsync(new InboundMessage("5511999990000", "wamid.new"));
        timeProvider.UtcNow = Now.AddHours(25);

        await client.SendTextMessageAsync("5511999990000", "Ainda na janela renovada");

        using var json = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.Equal(
            "wamid.new",
            json.RootElement.GetProperty("context").GetProperty("message_id").GetString());
    }

    [Fact]
    public async Task SendTextMessageAsync_RejectsFreeFormMessageWhenSessionIsClosed()
    {
        var handler = new TestHttpMessageHandler();
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ConversationSessionClosedException>(
            () => client.SendTextMessageAsync("5511999990000", "Mensagem livre"));

        Assert.Equal("5511999990000", exception.Recipient);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendTemplateMessageAsync_CanStartOutsideServiceWindow()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(SentMessageResponse("wamid.template"));
        var client = CreateClient(handler);

        var result = await client.SendTemplateMessageAsync(
            "5511999990000",
            "pedido_confirmado",
            "pt_BR",
            [
                new TemplateMessageComponent
                {
                    Type = "body",
                    Parameters =
                    [
                        new TemplateMessageParameter { Type = "text", Text = "Daniel" }
                    ]
                }
            ]);

        Assert.Equal("wamid.template", result.MessageId);
        using var json = JsonDocument.Parse(handler.Requests.Single().Body!);
        Assert.False(json.RootElement.TryGetProperty("context", out _));
        Assert.Equal("template", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "pedido_confirmado",
            json.RootElement.GetProperty("template").GetProperty("name").GetString());
        Assert.Equal(
            "pt_BR",
            json.RootElement.GetProperty("template").GetProperty("language").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_SendsTypedMediaAndCustomPayloads()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(SentMessageResponse("wamid.image"));
        handler.EnqueueJson(SentMessageResponse("wamid.interactive"));
        var client = CreateClient(handler);
        await client.RegisterInboundMessageAsync(new InboundMessage("5511999990000", "wamid.in"));

        await client.SendMessageAsync(new OutboundMessage(
            "5511999990000",
            new ImageMessageContent(link: new Uri("https://cdn.example.com/image.jpg"), caption: "Imagem")));
        await client.SendMessageAsync(new OutboundMessage(
            "5511999990000",
            new CustomMessageContent(
                "interactive",
                JsonSerializer.SerializeToElement(new { type = "button", body = new { text = "Escolha" } }))));

        using var imageJson = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal(
            "https://cdn.example.com/image.jpg",
            imageJson.RootElement.GetProperty("image").GetProperty("link").GetString());
        using var interactiveJson = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(
            "button",
            interactiveJson.RootElement.GetProperty("interactive").GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendMessageAsync_MapsMetaErrorDetails()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "error": {
                "message": "Invalid parameter",
                "type": "OAuthException",
                "code": 100,
                "error_subcode": 2494073,
                "fbtrace_id": "trace-123"
              }
            }
            """,
            HttpStatusCode.BadRequest);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<MetaWhatsAppApiException>(
            () => client.SendTemplateMessageAsync("5511999990000", "template", "pt_BR"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal(100, exception.ErrorCode);
        Assert.Equal(2494073, exception.ErrorSubcode);
        Assert.Equal("trace-123", exception.TraceId);
    }

    private static MetaWhatsAppClient CreateClient(
        TestHttpMessageHandler handler,
        TestTimeProvider? timeProvider = null) =>
        new(
            new HttpClient(handler),
            new MetaWhatsAppOptions
            {
                AccessToken = "access-token",
                PhoneNumberId = "phone-id",
                BusinessAccountId = "waba-id",
                GraphApiVersion = "v23.0"
            },
            timeProvider: timeProvider ?? new TestTimeProvider(Now));

    private static string SentMessageResponse(string messageId) =>
        $$"""
        {
          "contacts": [{ "input": "5511999990000", "wa_id": "5511999990000" }],
          "messages": [{ "id": "{{messageId}}" }]
        }
        """;
}
