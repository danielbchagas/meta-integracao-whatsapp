using System.Security.Cryptography;
using System.Text;
using Meta.WhatsApp.Exceptions;
using Meta.WhatsApp.Sessions;
using Meta.WhatsApp.Specs.Support;
using Meta.WhatsApp.Webhooks;
using Reqnroll;
using Xunit;

namespace Meta.WhatsApp.Specs.Bindings;

[Binding]
public sealed class WebhookSecuritySteps
{
    private const string Recipient = "5511999990000";
    private const string AppSecret = "app-secret";
    private static readonly DateTimeOffset InitialTime = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly ScenarioHttpMessageHandler _handler = new();
    private readonly InMemoryConversationSessionStore _store = new();
    private readonly SpecTimeProvider _timeProvider = new(InitialTime);
    private readonly MetaWhatsAppClient _client;
    private byte[]? _payload;
    private string? _signature;
    private MetaWebhookProcessingResult? _result;
    private Exception? _exception;

    public WebhookSecuritySteps()
    {
        _client = new MetaWhatsAppClient(
            new HttpClient(_handler),
            new MetaWhatsAppOptions
            {
                AccessToken = "access-token",
                PhoneNumberId = "phone-id",
                BusinessAccountId = "waba-id",
                GraphApiVersion = "v23.0"
            },
            _store,
            _timeProvider);
    }

    [Given("que existe uma sessão aguardando resposta ao template")]
    public async Task GivenThereIsASessionWaitingForTemplateReply()
    {
        await _client.RegisterInboundMessageAsync(
            new InboundMessage(Recipient, "wamid.inbound", _timeProvider.UtcNow));
        _timeProvider.Advance(TimeSpan.FromHours(25));
        _handler.EnqueueJson(ApprovedTemplateResponse());
        _handler.EnqueueJson(SentMessageResponse());
        await _client.ReengageAsync(new ReengagementRequest(
            Recipient,
            "retomar_atendimento",
            "pt_BR",
            "attempt-1"));
    }

    [Given("que a Meta enviará um webhook de resposta com assinatura válida")]
    public void GivenMetaWillSendAReplyWebhookWithValidSignature()
    {
        _payload = InboundPayload("phone-id");
        _signature = Sign(_payload);
    }

    [Given("que a Meta enviará um webhook de resposta com assinatura inválida")]
    public void GivenMetaWillSendAReplyWebhookWithInvalidSignature()
    {
        _payload = InboundPayload("phone-id");
        _signature = "sha256=00";
    }

    [Given("que a Meta enviará um webhook válido de outro canal")]
    public void GivenMetaWillSendAValidWebhookFromAnotherChannel()
    {
        _payload = InboundPayload("other-phone-id");
        _signature = Sign(_payload);
    }

    [When("o webhook seguro for processado")]
    public async Task WhenTheSecureWebhookIsProcessed()
    {
        _exception = null;
        try
        {
            _result = await _client.ProcessWebhookAsync(
                _payload ?? throw new InvalidOperationException("Webhook payload was not configured."),
                _signature,
                AppSecret);
        }
        catch (Exception exception)
        {
            _exception = exception;
        }
    }

    [Then("o processamento deve indicar que a sessão foi reativada")]
    public void ThenProcessingMustIndicateTheSessionWasReactivated()
    {
        Assert.Null(_exception);
        Assert.True(_result?.WasAnySessionReactivated);
    }

    [Then("a resposta processada deve estar correlacionada ao template")]
    public void ThenTheProcessedReplyMustBeCorrelatedToTheTemplate() =>
        Assert.True(Assert.Single(_result!.InboundRegistrations).IsReplyToReengagement);

    [Then("o webhook deve ser rejeitado por assinatura inválida")]
    public void ThenTheWebhookMustBeRejectedDueToInvalidSignature() =>
        Assert.IsType<MetaWebhookException>(_exception);

    [Then("a sessão deve continuar aguardando resposta")]
    public async Task ThenTheSessionMustRemainWaitingForReply()
    {
        var session = await _client.GetSessionAsync(Recipient);
        Assert.Equal(ConversationSessionState.ReengagementPending, session?.State);
    }

    [Then("a notificação deve ser ignorada")]
    public void ThenTheNotificationMustBeIgnored()
    {
        Assert.Null(_exception);
        Assert.Equal(1, _result?.IgnoredNotifications);
        Assert.Empty(_result!.InboundRegistrations);
    }

    [Then("nenhuma sessão deve ser criada pelo webhook")]
    public async Task ThenNoSessionMustBeCreatedByTheWebhook() =>
        Assert.Null(await _client.GetSessionAsync(Recipient));

    private byte[] InboundPayload(string channelId) =>
        Encoding.UTF8.GetBytes(
            $$$"""
            {
              "object":"whatsapp_business_account",
              "entry":[{"changes":[{"field":"messages","value":{
                "metadata":{"phone_number_id":"{{{channelId}}}"},
                "messages":[{
                  "from":"{{{Recipient}}}",
                  "id":"wamid.customer-reply",
                  "timestamp":"{{{_timeProvider.UtcNow.AddMinutes(1).ToUnixTimeSeconds()}}}",
                  "type":"text",
                  "context":{"id":"wamid.reengagement"},
                  "text":{"body":"Quero continuar"}
                }]
              }}]}]
            }
            """);

    private static string Sign(ReadOnlySpan<byte> payload) =>
        $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), payload)).ToLowerInvariant()}";

    private static string ApprovedTemplateResponse() =>
        """
        {"data":[{
          "id":"template-id", "name":"retomar_atendimento", "language":"pt_BR",
          "category":"UTILITY", "status":"APPROVED", "components":[]
        }]}
        """;

    private static string SentMessageResponse() =>
        $$"""
        {
          "contacts":[{"input":"{{Recipient}}","wa_id":"{{Recipient}}"}],
          "messages":[{"id":"wamid.reengagement"}]
        }
        """;
}
