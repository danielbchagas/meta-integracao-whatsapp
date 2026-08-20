using System.Net;
using System.Text.Json;
using Meta.WhatsApp.Exceptions;
using Meta.WhatsApp.Sessions;

namespace Meta.WhatsApp.Tests;

public sealed class MetaWhatsAppClientReengagementTests
{
    private const string Recipient = "5511999990000";
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReengageAsync_UsesSameChannelAndPreservesExpiredSession()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse());
        handler.EnqueueJson(SentMessageResponse("wamid.reengagement"));
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound"));
        timeProvider.UtcNow = Now.AddHours(25);

        var result = await client.ReengageAsync(Request("attempt-1"));

        Assert.Equal(ReengagementAction.Submitted, result.Action);
        Assert.Equal("phone-id", result.ChannelId);
        Assert.Equal("wamid.reengagement", result.MessageId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            "https://graph.facebook.com/v23.0/phone-id/messages",
            handler.Requests[1].Uri.ToString());
        using var requestJson = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.False(requestJson.RootElement.TryGetProperty("context", out _));

        var session = await client.GetSessionAsync(Recipient);
        Assert.NotNull(session);
        Assert.Equal(ConversationSessionState.ReengagementPending, session.State);
        Assert.Equal("phone-id", session.ChannelId);
        Assert.Equal("wamid.inbound", session.LastInboundMessageId);
        Assert.Equal("wamid.reengagement", session.LastReengagementAttempt?.MessageId);
    }

    [Fact]
    public async Task ReengageAsync_RequiresApprovedTemplate()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse(status: "PENDING"));
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound"));
        timeProvider.UtcNow = Now.AddHours(25);

        var exception = await Assert.ThrowsAsync<ReengagementTemplateNotApprovedException>(
            () => client.ReengageAsync(Request("attempt-1")));

        Assert.Equal("PENDING", exception.Status);
        Assert.Single(handler.Requests);
        var session = await client.GetSessionAsync(Recipient);
        Assert.Equal(ConversationSessionState.Expired, session?.State);
        Assert.Empty(session!.ReengagementAttempts);
    }

    [Fact]
    public async Task ReengageAsync_IsIdempotentAndEnforcesCooldown()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse());
        handler.EnqueueJson(SentMessageResponse("wamid.reengagement"));
        handler.EnqueueJson(ApprovedTemplateResponse());
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound"));
        timeProvider.UtcNow = Now.AddHours(25);

        await client.ReengageAsync(Request("same-key"));
        var duplicate = await client.ReengageAsync(Request("same-key"));

        Assert.Equal(ReengagementAction.AlreadySubmitted, duplicate.Action);
        Assert.Equal("wamid.reengagement", duplicate.MessageId);
        Assert.Equal(2, handler.Requests.Count);

        var cooldown = await Assert.ThrowsAsync<ReengagementCooldownException>(
            () => client.ReengageAsync(Request("different-key")));
        Assert.Equal(timeProvider.UtcNow.AddMinutes(5), cooldown.RetryAtUtc);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests, item => item.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task RegisterInboundMessageAsync_ReopensWindowOnlyAfterCustomerReply()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse());
        handler.EnqueueJson(SentMessageResponse("wamid.reengagement"));
        handler.EnqueueJson(SentMessageResponse("wamid.free-form"));
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.old-inbound"));
        timeProvider.UtcNow = Now.AddHours(25);
        await client.ReengageAsync(Request("attempt-1"));

        var beforeReply = await client.GetOpenSessionAsync(Recipient);
        Assert.Null(beforeReply);

        await client.RegisterReengagementStatusAsync(new ReengagementStatusUpdate(
            Recipient,
            "wamid.reengagement",
            ReengagementMessageStatus.Delivered));
        Assert.Null(await client.GetOpenSessionAsync(Recipient));
        await Assert.ThrowsAsync<ConversationSessionClosedException>(
            () => client.SendTextMessageAsync(Recipient, "Ainda não pode enviar texto livre"));

        await client.RegisterInboundMessageAsync(new InboundMessage(
            Recipient,
            "wamid.new-inbound",
            timeProvider.UtcNow.AddMinutes(1)));
        await client.SendTextMessageAsync(Recipient, "Nova resposta livre");

        var reopened = await client.GetOpenSessionAsync(Recipient);
        Assert.NotNull(reopened);
        Assert.Equal(ConversationSessionState.Open, reopened.State);
        Assert.Equal("wamid.new-inbound", reopened.LastInboundMessageId);
        Assert.Equal(ReengagementMessageStatus.Delivered, reopened.LastReengagementAttempt?.Status);
        using var textJson = JsonDocument.Parse(handler.Requests[2].Body!);
        Assert.Equal(
            "wamid.new-inbound",
            textJson.RootElement.GetProperty("context").GetProperty("message_id").GetString());
    }

    [Fact]
    public async Task RegisterReengagementStatusAsync_FailedReturnsSessionToExpired()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse());
        handler.EnqueueJson(SentMessageResponse("wamid.reengagement"));
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound"));
        timeProvider.UtcNow = Now.AddHours(25);
        await client.ReengageAsync(Request("attempt-1"));

        var session = await client.RegisterReengagementStatusAsync(new ReengagementStatusUpdate(
            Recipient,
            "wamid.reengagement",
            ReengagementMessageStatus.Failed,
            ErrorCode: "131047",
            ErrorMessage: "Re-engagement message failed"));

        Assert.Equal(ConversationSessionState.Expired, session?.State);
        Assert.Equal(ReengagementMessageStatus.Failed, session?.LastReengagementAttempt?.Status);
        Assert.Equal("131047", session?.LastReengagementAttempt?.ErrorCode);
    }

    [Fact]
    public async Task ReengageAsync_RecordsSynchronousMetaFailure()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse());
        handler.EnqueueJson(
            "{\"error\":{\"message\":\"Template rejected\",\"code\":132001}}",
            HttpStatusCode.BadRequest);
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound"));
        timeProvider.UtcNow = Now.AddHours(25);

        await Assert.ThrowsAsync<MetaWhatsAppApiException>(
            () => client.ReengageAsync(Request("attempt-1")));

        var session = await client.GetSessionAsync(Recipient);
        Assert.Equal(ConversationSessionState.Expired, session?.State);
        Assert.Equal(ReengagementMessageStatus.Failed, session?.LastReengagementAttempt?.Status);
        Assert.Equal("132001", session?.LastReengagementAttempt?.ErrorCode);
    }

    [Fact]
    public async Task ReengageAsync_PreventsDuplicatesAcrossClientInstances()
    {
        var handler = new TestHttpMessageHandler
        {
            Responder = request => request.Method == HttpMethod.Get
                ? TestHttpMessageHandler.JsonResponse(ApprovedTemplateResponse())
                : TestHttpMessageHandler.JsonResponse(SentMessageResponse("wamid.shared"))
        };
        var store = new InMemoryConversationSessionStore();
        var timeProvider = new TestTimeProvider(Now);
        var httpClient = new HttpClient(handler);
        var firstClient = CreateClient(httpClient, store, timeProvider);
        var secondClient = CreateClient(httpClient, store, timeProvider);
        await firstClient.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound"));
        timeProvider.UtcNow = Now.AddHours(25);

        var results = await Task.WhenAll(
            firstClient.ReengageAsync(Request("shared-key")),
            secondClient.ReengageAsync(Request("shared-key")));

        Assert.Contains(results, result => result.Action == ReengagementAction.Submitted);
        Assert.Contains(results, result => result.Action is ReengagementAction.InProgress or ReengagementAction.AlreadySubmitted);
        Assert.Single(handler.Requests, item => item.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ReengageAsync_DoesNotReuseSessionFromAnotherChannel()
    {
        var store = new InMemoryConversationSessionStore();
        var timeProvider = new TestTimeProvider(Now);
        var firstClient = CreateClient(
            new HttpClient(new TestHttpMessageHandler()),
            store,
            timeProvider,
            phoneNumberId: "channel-one");
        var secondHandler = new TestHttpMessageHandler();
        var secondClient = CreateClient(
            new HttpClient(secondHandler),
            store,
            timeProvider,
            phoneNumberId: "channel-two");
        await firstClient.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound"));
        timeProvider.UtcNow = Now.AddHours(25);

        await Assert.ThrowsAsync<ConversationSessionNotFoundException>(
            () => secondClient.ReengageAsync(Request("attempt-1")));
        Assert.Empty(secondHandler.Requests);
    }

    private static ReengagementRequest Request(string idempotencyKey) =>
        new(Recipient, "retomar_atendimento", "pt_BR", idempotencyKey);

    private static MetaWhatsAppClient CreateClient(
        TestHttpMessageHandler handler,
        TestTimeProvider timeProvider) =>
        CreateClient(new HttpClient(handler), new InMemoryConversationSessionStore(), timeProvider);

    private static MetaWhatsAppClient CreateClient(
        HttpClient httpClient,
        IConversationSessionStore store,
        TestTimeProvider timeProvider,
        string phoneNumberId = "phone-id") =>
        new(
            httpClient,
            new MetaWhatsAppOptions
            {
                AccessToken = "access-token",
                PhoneNumberId = phoneNumberId,
                BusinessAccountId = "waba-id",
                GraphApiVersion = "v23.0",
                ReengagementCooldown = TimeSpan.FromMinutes(5)
            },
            store,
            timeProvider);

    private static string ApprovedTemplateResponse(string status = "APPROVED") =>
        $$"""
        {
          "data": [{
            "id": "template-id",
            "name": "retomar_atendimento",
            "language": "pt_BR",
            "category": "UTILITY",
            "status": "{{status}}",
            "components": []
          }]
        }
        """;

    private static string SentMessageResponse(string messageId) =>
        $$"""
        {
          "contacts": [{ "input": "{{Recipient}}", "wa_id": "{{Recipient}}" }],
          "messages": [{ "id": "{{messageId}}" }]
        }
        """;
}
