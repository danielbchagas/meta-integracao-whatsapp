# Meta WhatsApp

Class library em .NET 8 para encapsular o envio de mensagens pela WhatsApp Cloud API e a administração de templates pela Graph API da Meta.

## O que está incluído

- Um `MetaWhatsAppClient` reutilizável e seguro para uso concorrente.
- Envio de texto, imagem, vídeo, áudio, documento, localização e templates.
- `CustomMessageContent` para tipos novos ou avançados, como mensagens interativas.
- Criação, consulta paginada e atualização de templates.
- `EnsureTemplateAsync`, que cria o template ausente, não faz nada quando já está igual e atualiza somente quando categoria ou componentes mudaram.
- Controle local da janela de atendimento aberta por uma mensagem recebida do cliente.
- Reengajamento pelo mesmo número WhatsApp após o fechamento da janela, usando template aprovado.
- Idempotência, cooldown e histórico de tentativas de reengajamento.
- Detecção atômica de abertura, renovação e reativação por mensagens recebidas.
- Deduplicação de webhooks recebidos e correlação opcional da resposta com o template enviado.
- Validação HMAC de `X-Hub-Signature-256`, challenge de configuração e parser tipado de webhooks.
- Atualização de entrega por webhooks sem reabrir incorretamente a janela.
- Classificação de erros transitórios e exposição de `Retry-After` sem retries inseguros de mensagens.
- Exceções com código, subcódigo e `fbtrace_id` retornados pela Meta.
- Nenhuma dependência de ASP.NET ou de um contêiner de injeção de dependência.

## Projetos

- `src/Meta.WhatsApp`: class library `net8.0`.
- `tests/Meta.WhatsApp.Tests`: testes do contrato HTTP, sessões, paginação e sincronização de templates.
- `tests/Meta.WhatsApp.Specs`: especificações de negócio ReqNRoll em português.

## Configuração

O token deve ter `whatsapp_business_messaging` para envio e `whatsapp_business_management` para administrar templates. Não grave o token no código-fonte; obtenha-o de um secret manager ou variável de ambiente.

```csharp
using Meta.WhatsApp;
using Meta.WhatsApp.Sessions;

var options = new MetaWhatsAppOptions
{
    AccessToken = configuration["Meta:AccessToken"]!,
    PhoneNumberId = configuration["Meta:PhoneNumberId"]!,
    BusinessAccountId = configuration["Meta:BusinessAccountId"]!,
    // Fixe deliberadamente a versão adotada pela aplicação e atualize-a de forma controlada.
    GraphApiVersion = configuration["Meta:GraphApiVersion"]!, // por exemplo, "v23.0"
    ReengagementCooldown = TimeSpan.FromMinutes(5),
    MaxReengagementHistory = 20,
    MaxInboundMessageHistory = 100
};

var client = new MetaWhatsAppClient(
    httpClient,
    options,
    new InMemoryConversationSessionStore());
```

O `HttpClient` deve ter ciclo de vida longo ou ser criado por `IHttpClientFactory` no projeto consumidor. A biblioteca não altera `BaseAddress` nem cabeçalhos padrão do objeto recebido.

## Sessões e janela de atendimento

A Meta não entrega um ID de sessão que possa ser enviado novamente. A janela de atendimento é associada ao destinatário e é renovada quando chega uma nova mensagem dele. O projeto representa essa janela localmente e guarda também o último `message_id` recebido.

Ao processar o webhook da Meta, registre a mensagem recebida:

```csharp
await client.RegisterInboundMessageAsync(new InboundMessage(
    Recipient: webhookMessage.From,
    MessageId: webhookMessage.Id,
    ReceivedAtUtc: webhookMessage.Timestamp));
```

Quando a aplicação precisa reagir à mudança de estado, use a versão detalhada. O resultado é calculado atomicamente pelo store e pode ser `Opened`, `Renewed`, `Reactivated`, `Duplicate` ou `IgnoredOutOfOrder`:

```csharp
var registration = await client.RegisterInboundMessageWithResultAsync(new InboundMessage(
    Recipient: webhookMessage.From,
    MessageId: webhookMessage.Id,
    ReceivedAtUtc: webhookMessage.Timestamp,
    ContextMessageId: webhookMessage.Context?.Id));

if (registration.WasReactivated)
{
    await eventPublisher.PublishAsync(new SessionReactivated(
        registration.Session.ChannelId,
        registration.Session.Recipient,
        registration.Session.ExpiresAtUtc));
}
```

`ContextMessageId` é opcional. Quando a resposta referencia diretamente um template de reengajamento enviado, `IsReplyToReengagement` será verdadeiro e `MatchedReengagementAttempt` identificará a tentativa correspondente. Uma nova mensagem digitada pelo cliente também reabre a janela, mesmo sem essa correlação explícita.

Notificações repetidas com o mesmo `MessageId` retornam `Duplicate` e não produzem outra reativação. Mensagens anteriores à última já processada retornam `IgnoredOutOfOrder`. A aplicação deve publicar seu evento de negócio somente para o resultado `Reactivated`.

### Endpoint de webhook

A biblioteca não depende de ASP.NET, mas fornece a validação e o processamento usados pelo endpoint hospedeiro. Leia o corpo como bytes e não o normalize antes de validar a assinatura:

```csharp
using Meta.WhatsApp.Webhooks;

var result = await client.ProcessWebhookAsync(
    payloadBytes,
    signatureHeader, // X-Hub-Signature-256
    configuration["Meta:AppSecret"]!,
    cancellationToken);

foreach (var registration in result.InboundRegistrations)
{
    if (registration.WasReactivated)
    {
        await eventPublisher.PublishAsync(new SessionReactivated(
            registration.Session.ChannelId,
            registration.Session.Recipient,
            registration.Session.ExpiresAtUtc));
    }
}
```

`ProcessWebhookAsync` valida a assinatura em tempo constante, interpreta `messages` e `statuses`, processa somente notificações do `ChannelId` configurado e informa quantas foram ignoradas. Para uma WABA com vários números, também é possível chamar `MetaWebhookParser.Parse`, usar `ChannelId` para selecionar o client correto e então processar a notificação tipada.

No `GET` de configuração do webhook, valide `hub.mode`, `hub.verify_token` e devolva o challenge somente quando `MetaWebhookChallengeVerifier.TryVerify` retornar `true`. Guarde tanto o verify token quanto o App Secret em um secret manager.

As mensagens livres seguintes consultam a mesma sessão e, por padrão, usam o último `message_id` como contexto de resposta:

```csharp
await client.SendTextMessageAsync("5511999990000", "Como posso ajudar?");
await client.SendTextMessageAsync("5511999990000", "Tenho mais uma informação.");
```

Fora da janela (24 horas por padrão), uma mensagem livre lança `ConversationSessionClosedException`. A sessão é preservada como `Expired`, permitindo um novo contato pelo mesmo canal. Para não exibir o contexto de resposta no WhatsApp durante uma janela aberta, configure `AttachReplyContextToOpenSession = false`; a validação da janela continua ativa.

`InMemoryConversationSessionStore` pode ser compartilhado por vários clients dentro do mesmo processo. Em produção com múltiplas réplicas, implemente `IConversationSessionStore` usando Redis ou banco compartilhado e preserve a atomicidade indicada pelos métodos de registro, reserva e atualização da interface. O histórico de IDs recebidos deve ser persistido junto da sessão para manter a deduplicação entre réplicas.

As sessões são isoladas pela combinação `PhoneNumberId + Recipient`. Um mesmo canal pode manter conversas abertas com vários clientes simultaneamente, e o mesmo destinatário em dois números WhatsApp diferentes não compartilha estado. O store em memória é thread-safe; stores distribuídos devem manter essa atomicidade por chave.

### Reengajamento após o fechamento

O reengajamento exige que já exista uma sessão expirada para a combinação `PhoneNumberId + Recipient`. O client consulta a Meta e só envia um template cujo estado seja `APPROVED`:

```csharp
var result = await client.ReengageAsync(new ReengagementRequest(
    Recipient: "5511999990000",
    TemplateName: "retomar_atendimento",
    LanguageCode: "pt_BR",
    IdempotencyKey: $"retomar-atendimento:{atendimentoId}",
    Components:
    [
        new TemplateMessageComponent
        {
            Type = "body",
            Parameters =
            [
                new TemplateMessageParameter { Type = "text", Text = "Daniel" }
            ]
        }
    ]));
```

A chave de idempotência deve identificar a operação de negócio e ser reutilizada em retries. Uma chamada repetida com a mesma chave não envia outra mensagem. Uma chave diferente durante o cooldown lança `ReengagementCooldownException`.

O envio bem-sucedido muda a sessão para `ReengagementPending`, mas não abre uma nova janela de texto livre. Eventos `sent`, `delivered`, `read` e `failed` do webhook podem ser registrados assim:

```csharp
await client.RegisterReengagementStatusAsync(new ReengagementStatusUpdate(
    Recipient: webhookStatus.RecipientId,
    MessageId: webhookStatus.Id,
    Status: ReengagementMessageStatus.Delivered,
    OccurredAtUtc: webhookStatus.Timestamp));
```

Somente o registro de uma mensagem recebida do cliente muda a sessão novamente para `Open`. `RegisterInboundMessageWithResultAsync` retorna `Reactivated` nessa transição. O template de reengajamento é enviado pelo mesmo `PhoneNumberId` e sem reutilizar o contexto vencido.

Antes de iniciar contatos, a aplicação consumidora deve garantir o opt-in aplicável e a categoria correta do template conforme as políticas da Meta.

### Falhas transitórias e retry

`MetaWhatsAppApiException` expõe `IsTransient` para `408`, `429` e respostas `5xx`, além de `RetryAfter` quando a Meta devolver esse cabeçalho. A biblioteca deliberadamente não repete automaticamente envios `POST`: uma falha observada pelo cliente não garante que a Meta deixou de aceitar a mensagem, portanto um retry cego pode duplicar o contato. A aplicação deve usar idempotência de negócio, consultar os webhooks de status e aplicar backoff antes de uma nova tentativa.

## Envio de mensagens

Template fora da janela de atendimento:

```csharp
using Meta.WhatsApp.Messages;

await client.SendTemplateMessageAsync(
    "5511999990000",
    "pedido_confirmado",
    "pt_BR",
    [
        new TemplateMessageComponent
        {
            Type = "body",
            Parameters =
            [
                new TemplateMessageParameter { Type = "text", Text = "Daniel" },
                new TemplateMessageParameter { Type = "text", Text = "12345" }
            ]
        }
    ]);
```

Imagem dentro de uma sessão aberta:

```csharp
await client.SendMessageAsync(new OutboundMessage(
    "5511999990000",
    new ImageMessageContent(
        link: new Uri("https://cdn.example.com/comprovante.jpg"),
        caption: "Seu comprovante")));
```

## Administração de templates

```csharp
using Meta.WhatsApp.Templates;

var definition = new TemplateDefinition
{
    Name = "pedido_confirmado",
    Language = "pt_BR",
    Category = "UTILITY",
    Components =
    [
        new TemplateComponent
        {
            Type = "BODY",
            Text = "Olá {{1}}, o pedido {{2}} foi confirmado."
        }
    ]
};

var synchronization = await client.EnsureTemplateAsync(definition);
// synchronization.Action: Created, Updated ou Unchanged

var allTemplates = await client.GetTemplatesAsync();
var templatesWithName = await client.GetTemplatesAsync("pedido_confirmado");
```

Criações são submetidas à análise da Meta. O endpoint de edição retorna apenas `success`; por isso, `TemplateSynchronizationResult.Status` é `null` após uma atualização. Consulte novamente o template pelo ID para obter o estado efetivo.

## Checklist para produção

- Registre `MetaWhatsAppClient` e `HttpClient` com ciclo de vida longo; configure timeout no projeto hospedeiro.
- Use um `IConversationSessionStore` persistente e compartilhado quando houver reinício de processo ou múltiplas réplicas. O `InMemoryConversationSessionStore` é apropriado somente para uma instância sem requisito de recuperação.
- Preserve a atomicidade de `RegisterInboundAsync`, incluindo o histórico de `MessageId`, e de `TryReserveReengagementAsync`.
- Valide o challenge e `X-Hub-Signature-256` antes de processar qualquer webhook.
- Use `MessageId` e `IdempotencyKey` como chaves idempotentes também na fila ou outbox do projeto consumidor.
- Só devolva sucesso ao webhook depois de registrar duravelmente o evento ou colocá-lo em uma fila durável.
- Monitore `MetaWhatsAppApiException.ErrorCode`, `TraceId`, `IsTransient`, `RetryAfter`, webhooks ignorados e sessões em `ReengagementPending`.
- Armazene Access Token, App Secret e verify token em um secret manager e estabeleça rotação.

## Build e testes

```powershell
dotnet build MetaIntegracaoWhatsApp.slnx
dotnet test MetaIntegracaoWhatsApp.slnx
```

Estado da validação desta versão:

- Build `Release` com analisadores .NET habilitados, warnings tratados como erro e nenhum diagnóstico pendente.
- 54 testes unitários.
- 55 cenários ReqNRoll.
- 109 testes aprovados no total.
- Pacote NuGet validado com `dotnet pack src/Meta.WhatsApp/Meta.WhatsApp.csproj -c Release`.

### Especificações BDD com ReqNRoll

O projeto `tests/Meta.WhatsApp.Specs` executa cenários Gherkin em português usando ReqNRoll com xUnit. As features exercitam o client real contra um `HttpMessageHandler` determinístico, sem acessar a Meta durante os testes:

- `CicloDaSessao.feature`: abertura, renovação, expiração, fechamento manual e isolamento por canal.
- `EnvioDeMensagens.feature`: texto, template, mídias, localização, payload customizado, contexto e erros.
- `Reengajamento.feature`: aprovação, idempotência, cooldown, falhas, resposta e concorrência.
- `StatusDoReengajamento.feature`: `sent`, `delivered`, `read`, `failed` e eventos fora de ordem.
- `RecebimentoDeWebhooks.feature`: assinatura, reativação autenticada e isolamento por canal.
- `GestaoDeTemplates.feature`: consulta, paginação, criação e atualização condicional.

Para executar apenas as especificações:

```powershell
dotnet test tests/Meta.WhatsApp.Specs/Meta.WhatsApp.Specs.csproj
```

As tags `@sessao`, `@mensagens`, `@reengajamento`, `@webhooks` e `@templates` identificam cada grupo no explorador de testes e nos relatórios ReqNRoll.

## Documentação oficial consultada

- [WhatsApp Cloud API — Messages](https://www.postman.com/meta/whatsapp-business-platform/folder/o48mro7/messages)
- [Webhook — objeto de mensagens recebidas](https://www.postman.com/meta/whatsapp-business-platform/folder/1dtuocp/messages-object)
- [Webhook — referência de payload](https://www.postman.com/meta/whatsapp-business-platform/folder/tduohwq/webhook-payload-reference)
- [Enviar mensagem de texto](https://www.postman.com/meta/whatsapp-business-platform/request/8gvd47s/send-text-message)
- [Enviar template](https://www.postman.com/meta/whatsapp-business-platform/request/o65u5m5/send-message-template-text)
- [WhatsApp Business Platform — Templates](https://www.postman.com/meta/whatsapp-business-platform/folder/lczy75a/templates)
- [Listar templates](https://www.postman.com/meta/whatsapp-business-platform/request/hl0hxc0/get-all-templates-default-fields)
- [Editar template](https://www.postman.com/meta/whatsapp-business-platform/request/bpcsm6i/edit-template)
- [ReqNRoll — configuração de build](https://docs.reqnroll.net/latest/installation/configuring-build.html)

As coleções acima são publicadas pela própria Meta. A versão da Graph API não é fixada pela biblioteca: deve ser informada pelo consumidor para evitar uma atualização silenciosa de contrato.
