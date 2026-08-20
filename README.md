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
- Atualização de entrega por webhooks sem reabrir incorretamente a janela.
- Exceções com código, subcódigo e `fbtrace_id` retornados pela Meta.
- Nenhuma dependência de ASP.NET ou de um contêiner de injeção de dependência.

## Projetos

- `src/Meta.WhatsApp`: class library `net8.0`.
- `tests/Meta.WhatsApp.Tests`: testes do contrato HTTP, sessões, paginação e sincronização de templates.

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
    MaxReengagementHistory = 20
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

As mensagens livres seguintes consultam a mesma sessão e, por padrão, usam o último `message_id` como contexto de resposta:

```csharp
await client.SendTextMessageAsync("5511999990000", "Como posso ajudar?");
await client.SendTextMessageAsync("5511999990000", "Tenho mais uma informação.");
```

Fora da janela (24 horas por padrão), uma mensagem livre lança `ConversationSessionClosedException`. A sessão é preservada como `Expired`, permitindo um novo contato pelo mesmo canal. Para não exibir o contexto de resposta no WhatsApp durante uma janela aberta, configure `AttachReplyContextToOpenSession = false`; a validação da janela continua ativa.

`InMemoryConversationSessionStore` pode ser compartilhado por vários clients dentro do mesmo processo. Em produção com múltiplas réplicas, implemente `IConversationSessionStore` usando Redis ou banco compartilhado e preserve a atomicidade indicada pelos métodos de reserva e atualização da interface.

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

Somente `RegisterInboundMessageAsync`, chamado quando o cliente responder, muda a sessão novamente para `Open`. O template de reengajamento é enviado pelo mesmo `PhoneNumberId` e sem reutilizar o contexto vencido.

Antes de iniciar contatos, a aplicação consumidora deve garantir o opt-in aplicável e a categoria correta do template conforme as políticas da Meta.

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

## Build e testes

```powershell
dotnet build MetaIntegracaoWhatsApp.slnx
dotnet test MetaIntegracaoWhatsApp.slnx
```

### Especificações BDD com ReqNRoll

O projeto `tests/Meta.WhatsApp.Specs` executa cenários Gherkin em português usando ReqNRoll com xUnit. As features exercitam o client real contra um `HttpMessageHandler` determinístico, sem acessar a Meta durante os testes:

- `CicloDaSessao.feature`: abertura, renovação, expiração, fechamento manual e isolamento por canal.
- `EnvioDeMensagens.feature`: texto, template, mídias, localização, payload customizado, contexto e erros.
- `Reengajamento.feature`: aprovação, idempotência, cooldown, falhas, resposta e concorrência.
- `StatusDoReengajamento.feature`: `sent`, `delivered`, `read`, `failed` e eventos fora de ordem.
- `GestaoDeTemplates.feature`: consulta, paginação, criação e atualização condicional.

Para executar apenas as especificações:

```powershell
dotnet test tests/Meta.WhatsApp.Specs/Meta.WhatsApp.Specs.csproj
```

As tags `@sessao`, `@mensagens`, `@reengajamento`, `@webhooks` e `@templates` identificam cada grupo no explorador de testes e nos relatórios ReqNRoll.

## Documentação oficial consultada

- [WhatsApp Cloud API — Messages](https://www.postman.com/meta/whatsapp-business-platform/folder/o48mro7/messages)
- [Enviar mensagem de texto](https://www.postman.com/meta/whatsapp-business-platform/request/8gvd47s/send-text-message)
- [Enviar template](https://www.postman.com/meta/whatsapp-business-platform/request/o65u5m5/send-message-template-text)
- [WhatsApp Business Platform — Templates](https://www.postman.com/meta/whatsapp-business-platform/folder/lczy75a/templates)
- [Listar templates](https://www.postman.com/meta/whatsapp-business-platform/request/hl0hxc0/get-all-templates-default-fields)
- [Editar template](https://www.postman.com/meta/whatsapp-business-platform/request/bpcsm6i/edit-template)
- [ReqNRoll — configuração de build](https://docs.reqnroll.net/latest/installation/configuring-build.html)

As coleções acima são publicadas pela própria Meta. A versão da Graph API não é fixada pela biblioteca: deve ser informada pelo consumidor para evitar uma atualização silenciosa de contrato.
