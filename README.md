# Meta WhatsApp

Solução .NET 8 para integrar sistemas com a WhatsApp Cloud API e a Graph API da Meta. O repositório contém um cliente de baixo nível distribuível como DLL, uma fachada de alto nível, uma API administrativa e um Worker para processamento assíncrono resiliente.

Os principais fluxos implementados são:

- cadastro e reconciliação de WABAs, números e templates;
- listagem de templates aprovados disponíveis para uso;
- criação, atualização e exclusão assíncronas de templates;
- envio idempotente de texto livre, template textual e template com imagem;
- geração de resumos em PNG, armazenamento no Blob e upload de mídia para a Meta;
- Outbox/Inbox, Azure Service Bus, retry, backoff e limite de tentativas;
- challenge, assinatura HMAC, parser e deduplicação de webhooks;
- acompanhamento de estados `Sent`, `Delivered`, `Read` e falha definitiva;
- reconciliação periódica de divergências com a Meta;
- health checks, logs JSON e erros HTTP em `ProblemDetails`.

## Arquitetura

| Projeto | Responsabilidade | Consumido por |
|---|---|---|
| `Meta.WhatsApp.Client` | Cliente leve da Cloud/Graph API, sessões, reengajamento e primitivas de webhook | aplicações que desejam acesso direto; `Sdk` |
| `Meta.WhatsApp.Domain` | Entidades, invariantes e máquinas de estado | `Application`, `Infrastructure` |
| `Meta.WhatsApp.Application` | Casos de uso, estratégias e contratos de persistência/integração | `Infrastructure`, `Sdk`, `Worker` |
| `Meta.WhatsApp.Infrastructure` | EF Core/SQL Server, Meta Graph API, Azure e renderização SkiaSharp | `Sdk`, `Worker` |
| `Meta.WhatsApp.Sdk` | Fachada `IMetaWhatsAppSdk` e composição DI das camadas inferiores | `Api` e consumidores .NET de alto nível |
| `Meta.WhatsApp.Api` | Endpoints HTTP de administração, mensagens e webhooks | clientes HTTP |
| `Meta.WhatsApp.Worker` | Outbox, Service Bus, Inbox, processadores e reconciliação | processo hospedado |

`Meta.WhatsApp.Client` e `Meta.WhatsApp.Sdk` têm propósitos diferentes. O Client não depende de EF Core ou Azure. O Sdk é a fachada de aplicação e referencia Client, Application e Infrastructure deliberadamente.

Os namespaces públicos do cliente permanecem `Meta.WhatsApp.*`; o assembly e o pacote são `Meta.WhatsApp.Client`.

## Estrutura do repositório

```text
src/
  Meta.WhatsApp.Client/
  Meta.WhatsApp.Domain/
  Meta.WhatsApp.Application/
  Meta.WhatsApp.Infrastructure/
  Meta.WhatsApp.Sdk/
  Meta.WhatsApp.Api/
  Meta.WhatsApp.Worker/
tests/
  Meta.WhatsApp.Client.Tests/
  Meta.WhatsApp.Client.Acceptance.Tests/
  Meta.WhatsApp.Domain.Tests/
  Meta.WhatsApp.Application.Tests/
  Meta.WhatsApp.Infrastructure.Tests/
  Meta.WhatsApp.Sdk.Tests/
  Meta.WhatsApp.Api.Tests/
  Meta.WhatsApp.Worker.Tests/
docs/
  plano-implementacao-whatsapp-meta-waba.md
  matriz-testes-funcionais.md
```

O diretório `docs` e todos os projetos estão incluídos em `MetaIntegracaoWhatsApp.slnx` e aparecem na árvore da IDE.

## Pré-requisitos

- .NET SDK 8 ou mais recente compatível com `net8.0`;
- SQL Server/Azure SQL para execução persistente;
- Azure Key Vault para tokens e segredos;
- Azure Service Bus com tópico e subscription;
- Azure Blob Storage para mídias renderizadas;
- identidade com permissões nos recursos Azure;
- uma WABA e um app Meta para chamadas reais.

Os testes locais usam EF InMemory e doubles dos serviços externos; não exigem contas Azure ou Meta.

## Início rápido

Restaure, compile e execute todos os testes:

```powershell
dotnet restore MetaIntegracaoWhatsApp.slnx
dotnet build MetaIntegracaoWhatsApp.slnx --no-restore
dotnet test MetaIntegracaoWhatsApp.slnx --no-build
```

Aplique as migrations usando a connection string padrão de desenvolvimento ou `WHATSAPP_SQL_CONNECTION`:

```powershell
$env:WHATSAPP_SQL_CONNECTION = "Server=(localdb)\mssqllocaldb;Database=WhatsAppMessaging;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update --project src/Meta.WhatsApp.Infrastructure/Meta.WhatsApp.Infrastructure.csproj
```

Execute API e Worker em terminais separados:

```powershell
dotnet run --project src/Meta.WhatsApp.Api/Meta.WhatsApp.Api.csproj
dotnet run --project src/Meta.WhatsApp.Worker/Meta.WhatsApp.Worker.csproj
```

## Configuração

API e Worker usam as mesmas seções de configuração:

```json
{
  "ConnectionStrings": {
    "WhatsApp": "Server=(localdb)\\mssqllocaldb;Database=WhatsAppMessaging;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Meta": {
    "GraphApiBaseUrl": "https://graph.facebook.com/",
    "ApiVersion": "v23.0"
  },
  "KeyVault": {
    "VaultUri": "https://meu-vault.vault.azure.net/",
    "SecretCacheMinutes": 5
  },
  "ServiceBus": {
    "FullyQualifiedNamespace": "meu-bus.servicebus.windows.net",
    "TopicName": "whatsapp-events",
    "SubscriptionName": "whatsapp-worker",
    "MaxConcurrentCalls": 8,
    "MaxDeliveryCount": 10
  },
  "Storage": {
    "ServiceUri": "https://minhaconta.blob.core.windows.net/",
    "Container": "whatsapp-media"
  },
  "Workers": {
    "OutboxBatchSize": 50,
    "PollIntervalSeconds": 2,
    "ClaimTimeoutSeconds": 120,
    "MaxAttempts": 10,
    "ReconciliationIntervalMinutes": 15
  },
  "Webhook": {
    "VerifyTokenSecretKey": "meta-webhook-verify-token",
    "AppSecretSecretKey": "meta-app-secret"
  }
}
```

Em ambientes hospedados, prefira variáveis de ambiente:

```text
ConnectionStrings__WhatsApp
Meta__GraphApiBaseUrl
Meta__ApiVersion
KeyVault__VaultUri
ServiceBus__FullyQualifiedNamespace
ServiceBus__TopicName
ServiceBus__SubscriptionName
Storage__ServiceUri
Storage__Container
Webhook__VerifyTokenSecretKey
Webhook__AppSecretSecretKey
```

### Credenciais e segredos

O cadastro de uma WABA recebe `credentialKey`, que é o nome do secret do access token no Key Vault. O token não é salvo no SQL, em eventos, logs ou arquivos de configuração.

Os secrets indicados por `Webhook:VerifyTokenSecretKey` e `Webhook:AppSecretSecretKey` também devem existir no Key Vault. A implementação usa `DefaultAzureCredential`, cacheia secrets por cinco minutos por padrão e não exige client secret quando Managed Identity ou credencial federada estiver configurada.

Permissões mínimas esperadas:

- Key Vault Secrets User para leitura dos secrets;
- Azure Service Bus Data Sender/Receiver;
- Storage Blob Data Contributor;
- acesso do serviço à base SQL;
- permissões Meta `whatsapp_business_messaging` e `whatsapp_business_management`.

## API HTTP

### Endpoints

| Método | Rota | Resultado |
|---|---|---|
| `POST` | `/api/wabas` | cadastra, valida e sincroniza uma WABA |
| `GET` | `/api/wabas/{wabaId}` | consulta a WABA |
| `GET` | `/api/wabas/{wabaId}/phone-numbers` | lista números sincronizados |
| `GET` | `/api/wabas/{wabaId}/templates` | lista todos os templates locais |
| `GET` | `/api/wabas/{wabaId}/templates/available` | lista apenas templates `APPROVED` e seus `messageTypes` habilitados |
| `GET` | `/api/wabas/{wabaId}/templates/{templateId}` | consulta um template |
| `POST` | `/api/wabas/{wabaId}/templates` | enfileira criação e retorna `202` |
| `PUT` | `/api/wabas/{wabaId}/templates/{templateId}` | enfileira atualização e retorna `202` |
| `DELETE` | `/api/wabas/{wabaId}/templates/{templateId}` | enfileira exclusão e retorna `202` |
| `GET` | `/api/operations/{operationId}` | consulta uma operação assíncrona |
| `POST` | `/api/messages` | enfileira uma mensagem; exige `Idempotency-Key` |
| `GET` | `/api/messages/{messageId}` | consulta estado e IDs da mensagem |
| `GET` | `/webhooks/meta` | valida o challenge da Meta |
| `POST` | `/webhooks/meta` | autentica e registra eventos recebidos |
| `GET` | `/health/live` | liveness sem dependências externas |
| `GET` | `/health/ready` | readiness do SQL, sem chamar a Meta |

### Cadastrar uma WABA

```http
POST /api/wabas
Content-Type: application/json
X-Correlation-Id: 11111111-1111-1111-1111-111111111111

{
  "wabaId": "123456789",
  "credentialKey": "meta-waba-producao-token"
}
```

O serviço valida o ID na Meta, busca números/templates em paralelo e persiste o snapshot e o evento de outbox na mesma unidade transacional.

### Listar templates disponíveis

```http
GET /api/wabas/123456789/templates/available
```

Exemplo de resposta:

```json
[
  {
    "id": "95a9da10-747c-41d0-9754-c251d0037fe1",
    "metaTemplateId": "987654321",
    "name": "proposta_aprovada",
    "language": "pt_BR",
    "category": "UTILITY",
    "messageTypes": ["PROPOSTA_APROVADA"]
  }
]
```

Templates aprovados sem uma definição interna habilitada também são retornados, com `messageTypes` vazio.

### Enviar uma mensagem

```http
POST /api/messages
Content-Type: application/json
Idempotency-Key: pedido:123:aviso
X-Correlation-Id: 22222222-2222-2222-2222-222222222222

{
  "wabaId": "123456789",
  "phoneNumberId": "456789123",
  "messageType": "AVISO_SIMPLES",
  "recipient": "5511999990000",
  "data": {
    "text": "Seu pedido foi aprovado.",
    "previewUrl": false
  }
}
```

A primeira requisição cria mensagem, operação e outbox. Repetir a mesma `Idempotency-Key` retorna o registro existente com `duplicate: true`, sem um segundo envio.

Definições iniciais:

| Código | Estratégia | Template | Renderer | Habilitado |
|---|---|---|---|---|
| `AVISO_SIMPLES` | `FreeText` | — | — | sim |
| `PROPOSTA_APROVADA` | `TextTemplate` | `proposta_aprovada` | — | sim |
| `RESUMO_PROPOSTAS` | `ImageTemplate` | `resumo_propostas` | `proposal-summary` | sim |
| `RESUMO_VEICULOS` | `ImageTemplate` | `resumo_veiculos` | `vehicle-summary` | não |

Mensagens baseadas em template só são aceitas quando o template correspondente está sincronizado como `APPROVED`.

### Criar template

```http
POST /api/wabas/123456789/templates
Content-Type: application/json

{
  "name": "pedido_aprovado",
  "language": "pt_BR",
  "category": "UTILITY",
  "components": [
    { "type": "BODY", "text": "Olá {{1}}, seu pedido foi aprovado." }
  ]
}
```

Operações de escrita retornam `202 Accepted` e uma URL em `Location`. Consulte `/api/operations/{operationId}` até `Succeeded`, `Failed` ou `FailedPermanent`.

### Webhook

O challenge usa `hub.mode`, `hub.verify_token` e `hub.challenge`. O `POST` exige `X-Hub-Signature-256: sha256=<hmac>` calculado sobre os bytes exatos do corpo com o App Secret.

- assinatura inválida: `401`;
- payload autenticado, mas malformado: `400`;
- evento novo registrado: `200` com `accepted`;
- repetição do mesmo evento: `200` com `duplicates`.

## Uso da fachada `Meta.WhatsApp.Sdk`

Em uma aplicação .NET com DI:

```csharp
using Meta.WhatsApp.Sdk;

services.AddMetaWhatsAppSdk(configuration);
```

Injete `IMetaWhatsAppSdk` para expor os casos de uso sem depender dos handlers concretos:

```csharp
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Sdk;

public sealed class Notifications(IMetaWhatsAppSdk whatsApp)
{
    public Task<SendNotificationResult> SendAsync(
        SendNotificationCommand command,
        CancellationToken cancellationToken) =>
        whatsApp.SendNotificationAsync(command, cancellationToken);
}
```

A fachada oferece cadastro/consulta de WABA, números, templates disponíveis, ciclo assíncrono de templates, envio/consulta de mensagens, operações e ingestão de payloads de webhook.

## Uso direto de `Meta.WhatsApp.Client`

Use o Client quando a aplicação precisa chamar a Meta diretamente e gerenciar localmente a janela de atendimento, sem a arquitetura SQL/Azure desta solução.

```csharp
using Meta.WhatsApp;
using Meta.WhatsApp.Sessions;

var options = new MetaWhatsAppOptions
{
    AccessToken = accessToken,
    PhoneNumberId = phoneNumberId,
    BusinessAccountId = wabaId,
    GraphApiVersion = "v23.0",
    ReengagementCooldown = TimeSpan.FromMinutes(5)
};

var client = new MetaWhatsAppClient(
    httpClient,
    options,
    new InMemoryConversationSessionStore());

await client.RegisterInboundMessageAsync(new InboundMessage(
    Recipient: "5511999990000",
    MessageId: "wamid.inbound",
    ReceivedAtUtc: DateTimeOffset.UtcNow));

var sent = await client.SendTextMessageAsync(
    "5511999990000",
    "Olá! Como posso ajudar?",
    previewUrl: false);
```

O `HttpClient` deve ter ciclo de vida longo ou ser criado por `IHttpClientFactory`. Para múltiplas réplicas, implemente `IConversationSessionStore` com armazenamento compartilhado; `InMemoryConversationSessionStore` é apropriado apenas para uma instância sem requisito de recuperação.

O Client também oferece:

- texto, imagem, vídeo, áudio, documento, localização e payload customizado;
- envio e administração de templates;
- `EnsureTemplateAsync` para criação/atualização condicional;
- reengajamento idempotente com cooldown;
- parser tipado, challenge e validação HMAC de webhooks;
- código, subcódigo, `fbtrace_id` e `Retry-After` em erros Meta.

Para empacotar a DLL/NuGet:

```powershell
dotnet pack src/Meta.WhatsApp.Client/Meta.WhatsApp.Client.csproj -c Release
```

## Processamento assíncrono

Fluxo de mensagem:

```text
API -> SQL (mensagem + operação + outbox)
    -> OutboxPublisherWorker
    -> Azure Service Bus
    -> ServiceBusConsumerWorker + inbox
    -> renderização/Blob/upload, quando necessário
    -> Meta Graph API
    -> webhook autenticado
    -> inbox/outbox + atualização de status
```

Características operacionais:

- claim da outbox com `UPDLOCK`, `READPAST` e `ROWLOCK` no SQL Server;
- timeout de claim para recuperação após queda do Worker;
- consumers idempotentes com Inbox;
- backoff exponencial e suporte a `Retry-After`;
- métodos HTTP inseguros não recebem retry automático do pipeline HTTP;
- máximo de dez tentativas operacionais por padrão;
- mensagens do Service Bus excedidas são enviadas à DLQ;
- reconciliação periódica remove números/templates ausentes e cria versões apenas quando o hash muda.

## Persistência

O `WhatsAppDbContext` contém:

- `whatsapp_waba`;
- `whatsapp_phone_number`;
- `whatsapp_template` e `whatsapp_template_version`;
- `whatsapp_message_definition`;
- `whatsapp_message`;
- `whatsapp_rendered_media`;
- `whatsapp_operation`;
- `integration_outbox` e `integration_inbox`.

Entidades com GUID recebem o identificador no domínio (`ValueGeneratedNever`). Agregados mutáveis usam `rowversion`, e índices únicos protegem chaves naturais e idempotência.

## Testes

```powershell
dotnet test MetaIntegracaoWhatsApp.slnx
```

Estado atual:

| Suíte | Casos |
|---|---:|
| Client unitários | 54 |
| Client ReqNRoll | 55 |
| Domain | 8 |
| Application | 25 |
| Infrastructure | 15 |
| Sdk | 2 |
| API | 7 |
| Worker | 7 |
| **Total** | **173** |

Os cenários cobrem sucesso, validação, recurso inexistente, idempotência, duplicidade, estados inválidos, falha transitória, falha definitiva, paginação, contratos externos, renderização e webhooks.

Consulte [docs/matriz-testes-funcionais.md](docs/matriz-testes-funcionais.md) para a matriz completa e para as fronteiras que exigem Azure/Meta reais.

## Containers

Gere as imagens a partir da raiz do repositório:

```powershell
docker build -f src/Meta.WhatsApp.Api/Dockerfile -t meta-whatsapp-api .
docker build -f src/Meta.WhatsApp.Worker/Dockerfile -t meta-whatsapp-worker .
```

As imagens não contêm segredos. Injete configuração e identidade em tempo de execução.

## Checklist de produção

- provisionar SQL, Service Bus, Blob e Key Vault em região compatível;
- configurar Managed Identity e RBAC mínimo;
- aplicar migrations antes de liberar tráfego;
- criar tópico/subscription e política de DLQ;
- cadastrar URL/challenge do webhook e validar HMAC;
- fixar conscientemente a versão da Graph API;
- manter a API e o Worker em múltiplas réplicas conforme a carga;
- monitorar 429/5xx, operações permanentes, tamanho/idade da outbox, DLQ e reconciliação;
- alertar para falhas de credencial, SQL, Blob e Service Bus;
- definir retenção de mensagens, eventos e blobs;
- nunca registrar access token, App Secret ou verify token.

## Documentação adicional

- [Plano de implementação](docs/plano-implementacao-whatsapp-meta-waba.md)
- [Matriz de testes funcionais](docs/matriz-testes-funcionais.md)
- [WhatsApp Cloud API — documentação oficial](https://developers.facebook.com/docs/whatsapp/cloud-api/)
- [WhatsApp Business Platform — coleção oficial](https://www.postman.com/meta/whatsapp-business-platform/overview)
- [EF Core migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)
- [Azure Service Bus](https://learn.microsoft.com/azure/service-bus-messaging/)
