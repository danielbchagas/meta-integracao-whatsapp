using System.Net;
using System.Text.Json;
using Meta.WhatsApp.Exceptions;
using Meta.WhatsApp.Messages;
using Meta.WhatsApp.Sessions;
using Meta.WhatsApp.Specs.Support;
using Meta.WhatsApp.Templates;
using Reqnroll;
using Xunit;

namespace Meta.WhatsApp.Specs.Bindings;

[Binding]
public sealed class ClientInteractionSteps
{
    private const string Recipient = "5511999990000";
    private const string PhoneNumberId = "phone-id";
    private static readonly DateTimeOffset InitialTime = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly ScenarioHttpMessageHandler _handler = new();
    private readonly InMemoryConversationSessionStore _store = new();
    private readonly SpecTimeProvider _timeProvider = new(InitialTime);
    private MetaWhatsAppClient _client;
    private MetaWhatsAppClient? _secondClient;
    private ConversationSession? _session;
    private ConversationSession? _otherChannelSession;
    private SendMessageResult? _messageResult;
    private ReengagementResult? _reengagementResult;
    private ReengagementResult? _secondReengagementResult;
    private IReadOnlyList<WhatsAppTemplate>? _templates;
    private WhatsAppTemplate? _template;
    private TemplateSynchronizationResult? _synchronization;
    private Exception? _exception;
    private string? _lastReengagementKey;

    public ClientInteractionSteps()
    {
        _client = CreateClient(PhoneNumberId, attachReplyContext: true);
    }

    [When("o cliente enviar a primeira mensagem")]
    public async Task WhenTheCustomerSendsTheFirstMessage() =>
        _session = await RegisterInboundAsync("wamid.inbound-1");

    [Given("que o cliente possui uma sessão aberta")]
    [Given("que o cliente possui uma sessão aberta no canal principal")]
    public async Task GivenTheCustomerHasAnOpenSession() =>
        _session = await RegisterInboundAsync("wamid.inbound-1");

    [Then("a sessão deve estar aberta")]
    public async Task ThenTheSessionMustBeOpen()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.NotNull(_session);
        Assert.Equal(ConversationSessionState.Open, _session.State);
        Assert.True(_session.IsOpen(_timeProvider.UtcNow));
    }

    [Then("a sessão deve pertencer ao mesmo canal e cliente")]
    public void ThenTheSessionMustBelongToTheSameChannelAndCustomer()
    {
        Assert.NotNull(_session);
        Assert.Equal(PhoneNumberId, _session.ChannelId);
        Assert.Equal(Recipient, _session.Recipient);
    }

    [Given("que a Meta aceitará duas mensagens")]
    public void GivenMetaWillAcceptTwoMessages()
    {
        EnqueueSentMessage("wamid.out-1");
        EnqueueSentMessage("wamid.out-2");
    }

    [Given("que a Meta aceitará uma mensagem")]
    public void GivenMetaWillAcceptOneMessage() => EnqueueSentMessage("wamid.out-1");

    [When("o sistema enviar duas mensagens de texto livre")]
    public async Task WhenTheSystemSendsTwoFreeFormMessages()
    {
        await _client.SendTextMessageAsync(Recipient, "Primeira resposta");
        await _client.SendTextMessageAsync(Recipient, "Segunda resposta");
    }

    [Then("as duas mensagens devem usar o último contexto recebido")]
    public void ThenBothMessagesMustUseTheLatestInboundContext()
    {
        Assert.Equal(2, MessageRequests.Count);
        Assert.All(MessageRequests, request =>
        {
            using var json = JsonDocument.Parse(request.Body!);
            Assert.Equal(
                "wamid.inbound-1",
                json.RootElement.GetProperty("context").GetProperty("message_id").GetString());
        });
    }

    [Then("duas mensagens devem ter sido enviadas para a Meta")]
    public void ThenTwoMessagesMustHaveBeenSentToMeta() => Assert.Equal(2, MessageRequests.Count);

    [Given("que se passaram 23 horas")]
    public void GivenTwentyThreeHoursHavePassed() => _timeProvider.Advance(TimeSpan.FromHours(23));

    [When("o cliente enviar uma nova mensagem")]
    public async Task WhenTheCustomerSendsANewMessage() =>
        _session = await RegisterInboundAsync("wamid.inbound-new");

    [When("se passarem mais 2 horas")]
    public void WhenTwoMoreHoursPass() => _timeProvider.Advance(TimeSpan.FromHours(2));

    [Then("a sessão deve continuar aberta")]
    public async Task ThenTheSessionMustRemainOpen() => await ThenTheSessionMustBeOpen();

    [Then("o contexto atual deve ser a nova mensagem recebida")]
    public async Task ThenTheCurrentContextMustBeTheNewInboundMessage()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal("wamid.inbound-new", _session?.LastInboundMessageId);
    }

    [Given("que a janela de atendimento expirou")]
    public async Task GivenTheCustomerServiceWindowExpired()
    {
        _timeProvider.Advance(TimeSpan.FromHours(25));
        _session = await _client.GetSessionAsync(Recipient);
    }

    [Given("que o cliente possui uma sessão expirada")]
    public async Task GivenTheCustomerHasAnExpiredSession()
    {
        await GivenTheCustomerHasAnOpenSession();
        await GivenTheCustomerServiceWindowExpired();
    }

    [When("o sistema tentar enviar texto livre")]
    public async Task WhenTheSystemTriesToSendFreeFormText() =>
        await CaptureAsync(() => _client.SendTextMessageAsync(Recipient, "Mensagem bloqueada"));

    [Then("o envio deve falhar porque a sessão está fechada")]
    public void ThenSendingMustFailBecauseTheSessionIsClosed() =>
        Assert.IsType<ConversationSessionClosedException>(_exception);

    [Then("a sessão deve estar preservada como expirada")]
    public async Task ThenTheSessionMustBePreservedAsExpired()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.NotNull(_session);
        Assert.Equal(ConversationSessionState.Expired, _session.State);
    }

    [Then("nenhuma mensagem deve ter sido enviada para a Meta")]
    public void ThenNoMessageMustHaveBeenSentToMeta() => Assert.Empty(MessageRequests);

    [When("o sistema fechar a sessão manualmente")]
    public async Task WhenTheSystemClosesTheSessionManually() =>
        await _client.CloseSessionAsync(Recipient);

    [When("chegar uma mensagem mais nova seguida de uma mais antiga")]
    public async Task WhenANewerMessageArrivesBeforeAnOlderOne()
    {
        var baseTime = _timeProvider.UtcNow;
        await _client.RegisterInboundMessageAsync(new InboundMessage(
            Recipient,
            "wamid.newest",
            baseTime.AddMinutes(2)));
        await _client.RegisterInboundMessageAsync(new InboundMessage(
            Recipient,
            "wamid.older",
            baseTime.AddMinutes(1)));
    }

    [Then("o contexto atual deve ser a mensagem mais nova")]
    public async Task ThenTheCurrentContextMustBeTheNewestMessage()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal("wamid.newest", _session?.LastInboundMessageId);
    }

    [When("outro canal consultar a sessão do cliente")]
    public async Task WhenAnotherChannelQueriesTheCustomerSession()
    {
        var otherClient = CreateClient("other-phone-id", attachReplyContext: true);
        _otherChannelSession = await otherClient.GetSessionAsync(Recipient);
    }

    [Then("o outro canal não deve encontrar uma sessão")]
    public void ThenTheOtherChannelMustNotFindASession() => Assert.Null(_otherChannelSession);

    [Given("que não existe sessão para o cliente")]
    public void GivenThereIsNoSessionForTheCustomer()
    {
    }

    [When("o sistema enviar um texto com pré-visualização de URL")]
    public async Task WhenTheSystemSendsTextWithUrlPreview() =>
        _messageResult = await _client.SendTextMessageAsync(
            Recipient,
            "Acesse https://example.com",
            previewUrl: true);

    [Then("a mensagem enviada deve ser do tipo texto")]
    public void ThenTheSentMessageMustBeText() => AssertMessageType("text");

    [Then("a pré-visualização de URL deve estar habilitada")]
    public void ThenUrlPreviewMustBeEnabled()
    {
        using var json = LastMessageJson();
        Assert.True(json.RootElement.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }

    [When("o sistema enviar diretamente um template")]
    public async Task WhenTheSystemDirectlySendsATemplate() =>
        await CaptureAsync(async () =>
        {
            _messageResult = await _client.SendTemplateMessageAsync(
                Recipient,
                "retomar_atendimento",
                "pt_BR");
        });

    [Then("a mensagem enviada deve ser do tipo template")]
    public void ThenTheSentMessageMustBeATemplate() => AssertMessageType("template");

    [Then("a mensagem enviada não deve possuir contexto de resposta")]
    public void ThenTheSentMessageMustNotHaveReplyContext()
    {
        using var json = LastMessageJson();
        Assert.False(json.RootElement.TryGetProperty("context", out _));
    }

    [When(@"^o sistema enviar uma mensagem do tipo (.*)$")]
    public async Task WhenTheSystemSendsAMessageOfType(string type)
    {
        MessageContent content = type switch
        {
            "imagem" => new ImageMessageContent(link: new Uri("https://cdn.example.com/image.jpg")),
            "vídeo" => new VideoMessageContent(id: "video-id"),
            "áudio" => new AudioMessageContent(id: "audio-id"),
            "documento" => new DocumentMessageContent(
                link: new Uri("https://cdn.example.com/file.pdf"),
                fileName: "file.pdf"),
            "localização" => new LocationMessageContent(-23.5505, -46.6333, "São Paulo"),
            "customizada" => new CustomMessageContent(
                "interactive",
                JsonSerializer.SerializeToElement(new { type = "button", body = new { text = "Escolha" } })),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
        _messageResult = await _client.SendMessageAsync(new OutboundMessage(Recipient, content));
    }

    [Then(@"^o payload deve conter o conteúdo (.*)$")]
    public void ThenThePayloadMustContainTheContent(string type)
    {
        var propertyName = type switch
        {
            "imagem" => "image",
            "vídeo" => "video",
            "áudio" => "audio",
            "documento" => "document",
            "localização" => "location",
            "customizada" => "interactive",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
        using var json = LastMessageJson();
        Assert.True(json.RootElement.TryGetProperty(propertyName, out _));
        Assert.Equal(propertyName, json.RootElement.GetProperty("type").GetString());
    }

    [When("o sistema responder explicitamente a outra mensagem")]
    public async Task WhenTheSystemExplicitlyRepliesToAnotherMessage() =>
        _messageResult = await _client.SendMessageAsync(new OutboundMessage(
            Recipient,
            new TextMessageContent("Resposta explícita"),
            ReplyToMessageId: "wamid.explicit"));

    [Then("a mensagem deve usar o contexto explícito")]
    public void ThenTheMessageMustUseTheExplicitContext()
    {
        using var json = LastMessageJson();
        Assert.Equal(
            "wamid.explicit",
            json.RootElement.GetProperty("context").GetProperty("message_id").GetString());
    }

    [Given("que o contexto automático está desabilitado")]
    public void GivenAutomaticContextIsDisabled() =>
        _client = CreateClient(PhoneNumberId, attachReplyContext: false);

    [When("o sistema enviar uma mensagem de texto livre")]
    public async Task WhenTheSystemSendsAFreeFormTextMessage() =>
        _messageResult = await _client.SendTextMessageAsync(Recipient, "Mensagem livre");

    [When(@"^o sistema enviar uma mídia com referência (.*)$")]
    public async Task WhenTheSystemSendsMediaWithReference(string reference)
    {
        var content = reference switch
        {
            "ausente" => new ImageMessageContent(),
            "duplicada" => new ImageMessageContent(
                id: "image-id",
                link: new Uri("https://cdn.example.com/image.jpg")),
            _ => throw new ArgumentOutOfRangeException(nameof(reference), reference, null)
        };
        await CaptureAsync(() => _client.SendMessageAsync(new OutboundMessage(Recipient, content)));
    }

    [Then("o envio deve falhar por argumento inválido")]
    public void ThenSendingMustFailDueToInvalidArgument() => Assert.IsType<ArgumentException>(_exception);

    [Given("que a Meta rejeitará a mensagem com erro estruturado")]
    public void GivenMetaWillRejectTheMessageWithStructuredError() =>
        _handler.EnqueueJson(MetaErrorResponse(), HttpStatusCode.BadRequest);

    [Then("a falha deve expor código subcódigo e trace da Meta")]
    public void ThenTheFailureMustExposeMetaDetails()
    {
        var exception = Assert.IsType<MetaWhatsAppApiException>(_exception);
        Assert.Equal(100, exception.ErrorCode);
        Assert.Equal(2494073, exception.ErrorSubcode);
        Assert.Equal("trace-123", exception.TraceId);
    }

    [Given("que o template de retomada está aprovado")]
    public void GivenTheReengagementTemplateIsApproved() =>
        _handler.EnqueueJson(TemplateListResponse("APPROVED"));

    [Given(@"^que o template de retomada possui status (.*)$")]
    public void GivenTheReengagementTemplateHasStatus(string status) =>
        _handler.EnqueueJson(status == "NOT_FOUND" ? "{\"data\":[]}" : TemplateListResponse(status));

    [When(@"^o sistema solicitar o reengajamento com a chave (.*)$")]
    public async Task WhenTheSystemRequestsReengagement(string idempotencyKey) =>
        await CaptureReengagementAsync(_client, idempotencyKey, firstResult: true);

    [Then("o reengajamento deve ser submetido pelo mesmo canal")]
    public void ThenReengagementMustBeSubmittedThroughTheSameChannel()
    {
        Assert.Null(_exception);
        Assert.NotNull(_reengagementResult);
        Assert.Equal(ReengagementAction.Submitted, _reengagementResult.Action);
        Assert.Equal(PhoneNumberId, _reengagementResult.ChannelId);
        Assert.EndsWith($"/{PhoneNumberId}/messages", MessageRequests.Single().Uri.AbsolutePath);
    }

    [Then("a sessão deve ficar aguardando resposta do cliente")]
    [Then("a sessão deve continuar aguardando resposta do cliente")]
    public async Task ThenTheSessionMustBeWaitingForTheCustomer()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal(ConversationSessionState.ReengagementPending, _session?.State);
        Assert.Null(await _client.GetOpenSessionAsync(Recipient));
    }

    [Then("o template não deve reutilizar o contexto vencido")]
    public void ThenTheTemplateMustNotReuseExpiredContext() => ThenTheSentMessageMustNotHaveReplyContext();

    [Then("o reengajamento deve falhar porque o template não está aprovado")]
    public void ThenReengagementMustFailBecauseTemplateIsNotApproved() =>
        Assert.IsType<ReengagementTemplateNotApprovedException>(_exception);

    [Then("nenhuma mensagem de reengajamento deve ter sido enviada")]
    public void ThenNoReengagementMessageMustHaveBeenSent() => Assert.Empty(MessageRequests);

    [Then("o reengajamento deve falhar porque a sessão não existe")]
    public void ThenReengagementMustFailBecauseSessionDoesNotExist() =>
        Assert.IsType<ConversationSessionNotFoundException>(_exception);

    [Then("o reengajamento deve falhar porque a sessão ainda está aberta")]
    public void ThenReengagementMustFailBecauseSessionIsStillOpen() =>
        Assert.IsType<ConversationSessionStillOpenException>(_exception);

    [When("o sistema solicitar duas vezes o reengajamento com a mesma chave")]
    public async Task WhenTheSystemRequestsReengagementTwiceWithTheSameKey()
    {
        await CaptureReengagementAsync(_client, "same-key", firstResult: true);
        await CaptureReengagementAsync(_client, "same-key", firstResult: false);
    }

    [Then("a segunda solicitação deve retornar o envio anterior")]
    public void ThenTheSecondRequestMustReturnThePreviousSend()
    {
        Assert.NotNull(_secondReengagementResult);
        Assert.Equal(ReengagementAction.AlreadySubmitted, _secondReengagementResult.Action);
        Assert.Equal(_reengagementResult?.MessageId, _secondReengagementResult.MessageId);
    }

    [Then("somente uma mensagem de reengajamento deve ter sido enviada")]
    public void ThenOnlyOneReengagementMessageMustHaveBeenSent() => Assert.Single(MessageRequests);

    [Given("que o cliente já foi reengajado")]
    public async Task GivenTheCustomerWasAlreadyReengaged()
    {
        await GivenTheCustomerHasAnExpiredSession();
        GivenTheReengagementTemplateIsApproved();
        GivenMetaWillAcceptOneMessage();
        await CaptureReengagementAsync(_client, "attempt-1", firstResult: true);
        Assert.Null(_exception);
    }

    [Then("o reengajamento deve falhar por cooldown")]
    public void ThenReengagementMustFailDueToCooldown() =>
        Assert.IsType<ReengagementCooldownException>(_exception);

    [Given("que o cooldown de reengajamento terminou")]
    public void GivenTheReengagementCooldownEnded() => _timeProvider.Advance(TimeSpan.FromMinutes(6));

    [Then("duas tentativas de reengajamento devem estar armazenadas")]
    public async Task ThenTwoReengagementAttemptsMustBeStored()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal(2, _session?.ReengagementAttempts.Count);
    }

    [Then("duas mensagens de reengajamento devem ter sido enviadas")]
    public void ThenTwoReengagementMessagesMustHaveBeenSent() => Assert.Equal(2, MessageRequests.Count);

    [Given("que a Meta rejeitará o reengajamento")]
    public void GivenMetaWillRejectReengagement() =>
        _handler.EnqueueJson(
            "{\"error\":{\"message\":\"Template rejected\",\"code\":132001}}",
            HttpStatusCode.BadRequest);

    [Then("a tentativa deve estar falha e a sessão expirada")]
    public async Task ThenTheAttemptMustBeFailedAndSessionExpired()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal(ConversationSessionState.Expired, _session?.State);
        Assert.Equal(ReengagementMessageStatus.Failed, _session?.LastReengagementAttempt?.Status);
    }

    [Given("que ocorrerá uma falha de transporte")]
    public void GivenATransportFailureWillOccur() =>
        _handler.EnqueueException(new HttpRequestException("Network unavailable"));

    [Then("a tentativa deve ficar com status desconhecido")]
    public async Task ThenTheAttemptMustHaveUnknownStatus()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal(ReengagementMessageStatus.Unknown, _session?.LastReengagementAttempt?.Status);
        Assert.Equal(ConversationSessionState.ReengagementPending, _session?.State);
    }

    [Then("uma repetição com a mesma chave não deve reenviar a mensagem")]
    public async Task ThenRetryingWithTheSameKeyMustNotResend()
    {
        var requestCount = MessageRequests.Count;
        await CaptureReengagementAsync(
            _client,
            _lastReengagementKey ?? throw new InvalidOperationException("No reengagement key was recorded."),
            firstResult: false);
        Assert.Equal(requestCount, MessageRequests.Count);
        Assert.Equal(ReengagementAction.InProgress, _secondReengagementResult?.Action);
    }

    [When("o cliente responder ao reengajamento")]
    [Given("que o cliente respondeu ao reengajamento")]
    public async Task WhenTheCustomerRepliesToReengagement() =>
        _session = await RegisterInboundAsync("wamid.customer-reply");

    [Then("o novo contexto deve ser a resposta do cliente")]
    public async Task ThenTheNewContextMustBeTheCustomerReply()
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal("wamid.customer-reply", _session?.LastInboundMessageId);
    }

    [Given("que duas instâncias compartilham o armazenamento de sessões")]
    public void GivenTwoInstancesShareTheSessionStore() =>
        _secondClient = CreateClient(PhoneNumberId, attachReplyContext: true);

    [When("as duas instâncias solicitarem o mesmo reengajamento simultaneamente")]
    public async Task WhenBothInstancesRequestTheSameReengagementConcurrently()
    {
        _handler.Responder = request => request.Method == HttpMethod.Get
            ? ScenarioHttpMessageHandler.JsonResponse(TemplateListResponse("APPROVED"))
            : ScenarioHttpMessageHandler.JsonResponse(SentMessageResponse("wamid.concurrent"));
        var results = await Task.WhenAll(
            Task.Run(() => _client.ReengageAsync(ReengagementRequest("concurrent-key"))),
            Task.Run(() => (_secondClient ?? throw new InvalidOperationException()).ReengageAsync(
                ReengagementRequest("concurrent-key"))));
        _reengagementResult = results[0];
        _secondReengagementResult = results[1];
    }

    [Then("uma instância deve observar o resultado já reservado")]
    public void ThenOneInstanceMustObserveTheReservedResult()
    {
        var results = new[] { _reengagementResult, _secondReengagementResult };
        Assert.Contains(results, result => result?.Action == ReengagementAction.Submitted);
        Assert.Contains(results, result =>
            result?.Action is ReengagementAction.InProgress or ReengagementAction.AlreadySubmitted);
    }

    [When(@"^o webhook informar o status (.*)$")]
    public async Task WhenTheWebhookReportsStatus(string status)
    {
        var parsedStatus = Enum.Parse<ReengagementMessageStatus>(status, ignoreCase: true);
        _session = await _client.RegisterReengagementStatusAsync(new ReengagementStatusUpdate(
            Recipient,
            "wamid.out-1",
            parsedStatus));
    }

    [Then(@"^a tentativa deve possuir o status (.*)$")]
    public async Task ThenTheAttemptMustHaveStatus(string status)
    {
        _session = await _client.GetSessionAsync(Recipient);
        Assert.Equal(
            Enum.Parse<ReengagementMessageStatus>(status, ignoreCase: true),
            _session?.LastReengagementAttempt?.Status);
    }

    [Then("texto livre deve continuar bloqueado")]
    public async Task ThenFreeFormTextMustRemainBlocked()
    {
        await CaptureAsync(() => _client.SendTextMessageAsync(Recipient, "Ainda bloqueado"));
        Assert.IsType<ConversationSessionClosedException>(_exception);
    }

    [When("o webhook informar falha com código e mensagem")]
    public async Task WhenTheWebhookReportsFailureWithDetails() =>
        _session = await _client.RegisterReengagementStatusAsync(new ReengagementStatusUpdate(
            Recipient,
            "wamid.out-1",
            ReengagementMessageStatus.Failed,
            ErrorCode: "131047",
            ErrorMessage: "Delivery failed"));

    [Then("os detalhes da falha devem ser preservados")]
    public void ThenFailureDetailsMustBePreserved()
    {
        Assert.Equal("131047", _session?.LastReengagementAttempt?.ErrorCode);
        Assert.Equal("Delivery failed", _session?.LastReengagementAttempt?.ErrorMessage);
    }

    [When("chegar um status para um wamid desconhecido")]
    public async Task WhenAStatusArrivesForAnUnknownWamid() =>
        _session = await _client.RegisterReengagementStatusAsync(new ReengagementStatusUpdate(
            Recipient,
            "wamid.unknown",
            ReengagementMessageStatus.Read));

    [Given("que a Meta possui duas páginas de templates")]
    public void GivenMetaHasTwoTemplatePages()
    {
        _handler.EnqueueJson(TemplatePage("template-1", "cursor-2"));
        _handler.EnqueueJson(TemplatePage("template-2", nextCursor: null));
    }

    [When("o sistema listar os templates")]
    public async Task WhenTheSystemListsTemplates() =>
        _templates = await _client.GetTemplatesAsync();

    [Then("todos os templates das duas páginas devem ser retornados")]
    public void ThenAllTemplatesFromBothPagesMustBeReturned()
    {
        Assert.Equal(2, _templates?.Count);
        Assert.Equal(2, _handler.Requests.Count(request => request.Method == HttpMethod.Get));
    }

    [Given("que a Meta possui um template aprovado")]
    public void GivenMetaHasAnApprovedTemplate() =>
        _handler.EnqueueJson(TemplateByIdResponse());

    [When("o sistema buscar o template pelo identificador")]
    public async Task WhenTheSystemGetsTemplateById() =>
        _template = await _client.GetTemplateByIdAsync("template-id");

    [Then("o template aprovado deve ser retornado")]
    public void ThenTheApprovedTemplateMustBeReturned()
    {
        Assert.NotNull(_template);
        Assert.Equal("template-id", _template.Id);
        Assert.Equal("APPROVED", _template.Status);
    }

    [Given("que o template desejado não existe na Meta")]
    public void GivenTheDesiredTemplateDoesNotExist()
    {
        _handler.EnqueueJson("{\"data\":[]}");
        _handler.EnqueueJson("{\"id\":\"new-id\",\"status\":\"PENDING\",\"category\":\"UTILITY\"}");
    }

    [Given("que o template desejado já existe com o mesmo conteúdo")]
    public void GivenTheDesiredTemplateAlreadyExistsWithSameContent() =>
        _handler.EnqueueJson(DesiredTemplateListResponse("Olá {{1}}, seu pedido foi confirmado."));

    [Given("que o template desejado existe com conteúdo diferente")]
    public void GivenTheDesiredTemplateExistsWithDifferentContent()
    {
        _handler.EnqueueJson(DesiredTemplateListResponse("Conteúdo antigo"));
        _handler.EnqueueJson("{\"success\":true}");
    }

    [When("o sistema garantir a existência do template")]
    public async Task WhenTheSystemEnsuresTheTemplateExists() =>
        _synchronization = await _client.EnsureTemplateAsync(DesiredTemplate());

    [Then("o template deve ser criado")]
    public void ThenTheTemplateMustBeCreated() =>
        Assert.Equal(TemplateSynchronizationAction.Created, _synchronization?.Action);

    [Then("o template deve permanecer inalterado")]
    public void ThenTheTemplateMustRemainUnchanged() =>
        Assert.Equal(TemplateSynchronizationAction.Unchanged, _synchronization?.Action);

    [Then("nenhuma atualização de template deve ser enviada")]
    public void ThenNoTemplateUpdateMustBeSent() =>
        Assert.DoesNotContain(_handler.Requests, request => request.Method == HttpMethod.Post);

    [Then("o template deve ser atualizado")]
    public void ThenTheTemplateMustBeUpdated()
    {
        Assert.Equal(TemplateSynchronizationAction.Updated, _synchronization?.Action);
        Assert.Contains(_handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Uri.AbsolutePath.EndsWith("/template-id", StringComparison.Ordinal));
    }

    [Given("que a Meta rejeitará a criação do template")]
    public void GivenMetaWillRejectTemplateCreation() =>
        _handler.EnqueueJson(MetaErrorResponse(), HttpStatusCode.BadRequest);

    [When("o sistema criar o template")]
    public async Task WhenTheSystemCreatesTheTemplate() =>
        await CaptureAsync(() => _client.CreateTemplateAsync(DesiredTemplate()));

    [Then("a falha de template deve preservar os detalhes da Meta")]
    public void ThenTemplateFailureMustPreserveMetaDetails() => ThenTheFailureMustExposeMetaDetails();

    private IReadOnlyList<ScenarioHttpRequest> MessageRequests =>
        _handler.Requests
            .Where(request =>
                request.Method == HttpMethod.Post &&
                request.Uri.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
            .ToArray();

    private MetaWhatsAppClient CreateClient(string phoneNumberId, bool attachReplyContext) =>
        new(
            new HttpClient(_handler),
            new MetaWhatsAppOptions
            {
                AccessToken = "access-token",
                PhoneNumberId = phoneNumberId,
                BusinessAccountId = "waba-id",
                GraphApiVersion = "v23.0",
                AttachReplyContextToOpenSession = attachReplyContext,
                ReengagementCooldown = TimeSpan.FromMinutes(5)
            },
            _store,
            _timeProvider);

    private Task<ConversationSession> RegisterInboundAsync(string messageId) =>
        _client.RegisterInboundMessageAsync(new InboundMessage(Recipient, messageId, _timeProvider.UtcNow));

    private void EnqueueSentMessage(string messageId) =>
        _handler.EnqueueJson(SentMessageResponse(messageId));

    private async Task CaptureAsync(Func<Task> action)
    {
        _exception = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _exception = exception;
        }
    }

    private async Task CaptureReengagementAsync(
        MetaWhatsAppClient client,
        string idempotencyKey,
        bool firstResult)
    {
        _exception = null;
        _lastReengagementKey = idempotencyKey;
        try
        {
            var result = await client.ReengageAsync(ReengagementRequest(idempotencyKey));
            if (firstResult)
            {
                _reengagementResult = result;
            }
            else
            {
                _secondReengagementResult = result;
            }
        }
        catch (Exception exception)
        {
            _exception = exception;
        }
    }

    private static ReengagementRequest ReengagementRequest(string idempotencyKey) =>
        new(Recipient, "retomar_atendimento", "pt_BR", idempotencyKey);

    private void AssertMessageType(string expectedType)
    {
        using var json = LastMessageJson();
        Assert.Equal(expectedType, json.RootElement.GetProperty("type").GetString());
    }

    private JsonDocument LastMessageJson()
    {
        var request = Assert.Single(MessageRequests);
        return JsonDocument.Parse(request.Body!);
    }

    private static TemplateDefinition DesiredTemplate() => new()
    {
        Name = "pedido_confirmado",
        Language = "pt_BR",
        Category = "UTILITY",
        Components =
        [
            new TemplateComponent
            {
                Type = "BODY",
                Text = "Olá {{1}}, seu pedido foi confirmado."
            }
        ]
    };

    private static string SentMessageResponse(string messageId) =>
        $$"""
        {
          "contacts": [{ "input": "{{Recipient}}", "wa_id": "{{Recipient}}" }],
          "messages": [{ "id": "{{messageId}}" }]
        }
        """;

    private static string TemplateListResponse(string status) =>
        $$"""
        {
          "data": [{
            "id": "reengagement-template-id",
            "name": "retomar_atendimento",
            "language": "pt_BR",
            "category": "UTILITY",
            "status": "{{status}}",
            "components": []
          }]
        }
        """;

    private static string MetaErrorResponse() =>
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
        """;

    private static string TemplatePage(string id, string? nextCursor) =>
        $$"""
        {
          "data": [{
            "id":"{{id}}", "name":"{{id}}", "language":"pt_BR",
            "category":"UTILITY", "status":"APPROVED", "components":[]
          }]{{(nextCursor is null ? string.Empty : $$"""
          ,"paging": {
            "cursors": { "after":"{{nextCursor}}" },
            "next":"https://graph.facebook.com/v23.0/waba-id/message_templates?after={{nextCursor}}"
          }
          """)}}
        }
        """;

    private static string TemplateByIdResponse() =>
        """
        {
          "id":"template-id", "name":"pedido_confirmado", "language":"pt_BR",
          "category":"UTILITY", "status":"APPROVED", "components":[]
        }
        """;

    private static string DesiredTemplateListResponse(string text) =>
        $$"""
        {
          "data": [{
            "id": "template-id",
            "name": "pedido_confirmado",
            "language": "pt_BR",
            "category": "UTILITY",
            "status": "APPROVED",
            "components": [{ "type": "BODY", "text": {{JsonSerializer.Serialize(text)}} }]
          }]
        }
        """;
}
