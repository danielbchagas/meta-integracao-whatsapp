using System.Security.Cryptography;
using System.Text;
using Meta.WhatsApp.Exceptions;
using Meta.WhatsApp.Sessions;
using Meta.WhatsApp.Webhooks;

namespace Meta.WhatsApp.Tests;

public sealed class MetaWebhookTests
{
    private const string Recipient = "5511999990000";
    private const string AppSecret = "app-secret";
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SignatureValidator_AcceptsAuthenticPayloadAndRejectsTampering()
    {
        var payload = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\",\"entry\":[]}");
        var signature = Signature(payload);

        Assert.True(MetaWebhookSignatureValidator.IsValid(payload, signature, AppSecret));
        Assert.False(MetaWebhookSignatureValidator.IsValid(
            Encoding.UTF8.GetBytes("{\"object\":\"tampered\",\"entry\":[]}"),
            signature,
            AppSecret));
        Assert.False(MetaWebhookSignatureValidator.IsValid(payload, "sha256=invalid", AppSecret));
    }

    [Fact]
    public void ChallengeVerifier_ReturnsChallengeOnlyForExpectedToken()
    {
        Assert.True(MetaWebhookChallengeVerifier.TryVerify(
            "subscribe",
            "verify-token",
            "challenge-value",
            "verify-token",
            out var challenge));
        Assert.Equal("challenge-value", challenge);

        Assert.False(MetaWebhookChallengeVerifier.TryVerify(
            "subscribe",
            "wrong-token",
            "challenge-value",
            "verify-token",
            out challenge));
        Assert.Null(challenge);
    }

    [Fact]
    public void Parser_ExtractsInboundMessagesStatusesAndFailureDetails()
    {
        var notification = MetaWebhookParser.Parse(
            $$"""
            {
              "object": "whatsapp_business_account",
              "entry": [{
                "changes": [{
                  "field": "messages",
                  "value": {
                    "metadata": { "phone_number_id": "phone-id" },
                    "messages": [{
                      "from": "{{Recipient}}",
                      "id": "wamid.customer-reply",
                      "timestamp": "{{Now.ToUnixTimeSeconds()}}",
                      "type": "button",
                      "context": { "id": "wamid.reengagement" },
                      "button": { "payload": "continuar", "text": "Continuar" }
                    }],
                    "statuses": [{
                      "id": "wamid.reengagement",
                      "recipient_id": "{{Recipient}}",
                      "status": "failed",
                      "timestamp": {{Now.AddMinutes(1).ToUnixTimeSeconds()}},
                      "errors": [{
                        "code": 131047,
                        "error_data": { "details": "Delivery failed" }
                      }]
                    }]
                  }
                }]
              }]
            }
            """);

        var inbound = Assert.Single(notification.InboundMessages);
        Assert.Equal("phone-id", inbound.ChannelId);
        Assert.Equal(Recipient, inbound.Recipient);
        Assert.Equal("button", inbound.Type);
        Assert.Equal("wamid.reengagement", inbound.ContextMessageId);
        Assert.Equal("Continuar", inbound.Payload.GetProperty("button").GetProperty("text").GetString());

        var status = Assert.Single(notification.StatusUpdates);
        Assert.Equal("131047", status.ErrorCode);
        Assert.Equal("Delivery failed", status.ErrorMessage);
        Assert.True(status.TryCreateReengagementStatusUpdate(out var update));
        Assert.Equal(ReengagementMessageStatus.Failed, update?.Status);
    }

    [Fact]
    public async Task ProcessWebhookAsync_AuthenticatesReactivatesAndDeduplicatesCustomerReply()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse());
        handler.EnqueueJson(SentMessageResponse("wamid.reengagement"));
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound", Now));
        timeProvider.UtcNow = Now.AddHours(25);
        await client.ReengageAsync(new ReengagementRequest(
            Recipient,
            "retomar_atendimento",
            "pt_BR",
            "attempt-1"));
        var payload = InboundPayload(timeProvider.UtcNow.AddMinutes(1));
        var signature = Signature(payload);

        var first = await client.ProcessWebhookAsync(payload, signature, AppSecret);
        var duplicate = await client.ProcessWebhookAsync(payload, signature, AppSecret);

        var firstRegistration = Assert.Single(first.InboundRegistrations);
        Assert.True(firstRegistration.WasReactivated);
        Assert.True(firstRegistration.IsReplyToReengagement);
        Assert.True(first.WasAnySessionReactivated);
        Assert.Equal(
            InboundRegistrationOutcome.Duplicate,
            Assert.Single(duplicate.InboundRegistrations).Outcome);
        Assert.False(duplicate.WasAnySessionReactivated);
    }

    [Fact]
    public async Task ProcessWebhookAsync_RejectsInvalidSignatureBeforeChangingSession()
    {
        var client = CreateClient(new TestHttpMessageHandler(), new TestTimeProvider(Now));
        var payload = InboundPayload(Now);

        await Assert.ThrowsAsync<MetaWebhookException>(
            () => client.ProcessWebhookAsync(payload, "sha256=00", AppSecret));

        Assert.Null(await client.GetSessionAsync(Recipient));
    }

    [Fact]
    public async Task ProcessWebhookAsync_RegistersKnownStatusAndIgnoresUnsupportedStatus()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ApprovedTemplateResponse());
        handler.EnqueueJson(SentMessageResponse("wamid.reengagement"));
        var timeProvider = new TestTimeProvider(Now);
        var client = CreateClient(handler, timeProvider);
        await client.RegisterInboundMessageAsync(new InboundMessage(Recipient, "wamid.inbound", Now));
        timeProvider.UtcNow = Now.AddHours(25);
        await client.ReengageAsync(new ReengagementRequest(
            Recipient,
            "retomar_atendimento",
            "pt_BR",
            "attempt-1"));
        var notification = MetaWebhookParser.Parse(
            $$$"""
            {
              "object":"whatsapp_business_account",
              "entry":[{"changes":[{"field":"messages","value":{
                "metadata":{"phone_number_id":"phone-id"},
                "statuses":[
                  {
                    "id":"wamid.reengagement",
                    "recipient_id":"{{{Recipient}}}",
                    "status":"delivered",
                    "timestamp":"{{{timeProvider.UtcNow.AddMinutes(1).ToUnixTimeSeconds()}}}"
                  },
                  {
                    "id":"wamid.reengagement",
                    "recipient_id":"{{{Recipient}}}",
                    "status":"deleted",
                    "timestamp":"{{{timeProvider.UtcNow.AddMinutes(2).ToUnixTimeSeconds()}}}"
                  }
                ]
              }}]}]
            }
            """);

        var result = await client.ProcessWebhookAsync(notification);

        Assert.Single(result.StatusUpdates);
        Assert.Equal(1, result.IgnoredNotifications);
        Assert.Equal(
            ReengagementMessageStatus.Delivered,
            (await client.GetSessionAsync(Recipient))?.LastReengagementAttempt?.Status);
    }

    [Fact]
    public void Parser_RejectsMalformedRequiredFields()
    {
        var exception = Assert.Throws<MetaWebhookException>(() => MetaWebhookParser.Parse(
            """
            {
              "object":"whatsapp_business_account",
              "entry":[{"changes":[{"field":"messages","value":{"messages":[]}}]}]
            }
            """));

        Assert.Contains("metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MetaWhatsAppClient CreateClient(
        TestHttpMessageHandler handler,
        TestTimeProvider timeProvider) =>
        new(
            new HttpClient(handler),
            new MetaWhatsAppOptions
            {
                AccessToken = "access-token",
                PhoneNumberId = "phone-id",
                BusinessAccountId = "waba-id",
                GraphApiVersion = "v23.0"
            },
            new InMemoryConversationSessionStore(),
            timeProvider);

    private static byte[] InboundPayload(DateTimeOffset receivedAtUtc) =>
        Encoding.UTF8.GetBytes(
            $$"""
            {
              "object":"whatsapp_business_account",
              "entry":[{
                "changes":[{
                  "field":"messages",
                  "value":{
                    "metadata":{"phone_number_id":"phone-id"},
                    "messages":[{
                      "from":"{{Recipient}}",
                      "id":"wamid.customer-reply",
                      "timestamp":"{{receivedAtUtc.ToUnixTimeSeconds()}}",
                      "type":"text",
                      "context":{"id":"wamid.reengagement"},
                      "text":{"body":"Quero continuar"}
                    }]
                  }
                }]
              }]
            }
            """);

    private static string Signature(ReadOnlySpan<byte> payload) =>
        $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), payload)).ToLowerInvariant()}";

    private static string ApprovedTemplateResponse() =>
        """
        {
          "data": [{
            "id":"template-id", "name":"retomar_atendimento", "language":"pt_BR",
            "category":"UTILITY", "status":"APPROVED", "components":[]
          }]
        }
        """;

    private static string SentMessageResponse(string messageId) =>
        $$"""
        {
          "contacts":[{"input":"{{Recipient}}","wa_id":"{{Recipient}}"}],
          "messages":[{"id":"{{messageId}}"}]
        }
        """;
}
