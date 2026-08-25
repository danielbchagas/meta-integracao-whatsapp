# Plano de Implementação — WhatsApp Messaging Service (Meta / WABA)

## 1. Objetivo

Implementar, em uma solução existente, um microsserviço resiliente para integração com a Meta / WhatsApp Business Platform, tendo como ponto central uma **WABA (WhatsApp Business Account)**.

A solução deverá ser capaz de:

- cadastrar e sincronizar WABAs;
- autenticar de forma segura usando credenciais da Meta;
- recuperar e persistir números vinculados à WABA;
- recuperar e persistir templates;
- criar, atualizar, excluir e consultar templates;
- enviar mensagens de texto livre quando permitido;
- enviar mensagens utilizando templates aprovados;
- gerar imagens PNG/JPG para conteúdos estruturados ou tabulares;
- armazenar e gerenciar mídias;
- enviar imagens por templates com header de mídia;
- processar webhooks da Meta;
- utilizar Outbox e Inbox;
- garantir idempotência;
- implementar retries, circuit breaker e rate limiting;
- trabalhar com DLQ;
- realizar reconciliação periódica com a Meta;
- manter auditoria, observabilidade e rastreabilidade de ponta a ponta.

---

# 2. Princípios arquiteturais

## 2.1. WABA não é credencial

O `WabaId` apenas identifica uma WhatsApp Business Account.

A autenticação deve ser feita com uma credencial válida da Meta, preferencialmente um **System User Access Token**, armazenado fora do banco operacional.

Recomendação:

```text
WABA
  │
  ├── MetaWabaId
  ├── BusinessId
  └── CredentialKey
           │
           ▼
     Azure Key Vault
           │
           └── Access Token
```

Nunca persistir o token em:

- banco operacional;
- Outbox;
- logs;
- eventos;
- `appsettings.json`;
- mensagens do Service Bus.

---

## 2.2. Estado operacional não deve existir apenas na Outbox

A Outbox representa eventos/comandos aguardando publicação ou processamento.

O estado atual da integração deve ficar em tabelas específicas:

```text
whatsapp_waba
whatsapp_phone_number
whatsapp_template
whatsapp_template_version
whatsapp_message_definition
whatsapp_message
whatsapp_rendered_media
whatsapp_operation
```

A mesma transação que altera o estado gera o registro correspondente na Outbox.

```text
BEGIN TRANSACTION

UPDATE estado de domínio

INSERT integration_outbox

COMMIT
```

---

## 2.3. Separar intenção de negócio da implementação da Meta

Os sistemas consumidores não devem decidir se uma mensagem será:

- texto livre;
- template;
- PNG;
- JPEG;
- Media ID;
- template com header de imagem.

Eles devem informar apenas a intenção.

Exemplo:

```json
{
  "messageType": "RESUMO_PROPOSTAS",
  "recipient": "5527999999999",
  "data": {
    "propostas": []
  }
}
```

A decisão de renderização pertence ao WhatsApp Messaging Service.

---

# 3. Arquitetura alvo

```text
                            SISTEMAS INTERNOS
                                   │
                                   │
                            SendNotification
                                   │
                                   ▼
                    ┌──────────────────────────┐
                    │ WhatsApp Messaging API   │
                    └─────────────┬────────────┘
                                  │
                   ┌──────────────┼───────────────┐
                   │              │               │
                   ▼              ▼               ▼
                WABA Mgmt    Template Mgmt     Messaging
                   │              │               │
                   │              │        MessageDefinition
                   │              │               │
                   │              │     ┌─────────┼─────────┐
                   │              │     │         │         │
                   │              │     ▼         ▼         ▼
                   │              │  FreeText TextTpl  ImageTpl
                   │              │                         │
                   │              │                         ▼
                   │              │                  Content Renderer
                   │              │                         │
                   │              │                         ▼
                   │              │                    PNG / JPG
                   │              │                         │
                   │              │                         ▼
                   │              │                  Azure Blob Storage
                   │              │
                   └──────────────┴───────────────┐
                                                  ▼
                                             Azure SQL
                                      ┌───────────┴──────────┐
                                      │ Estado + Outbox      │
                                      └───────────┬──────────┘
                                                  │
                                                  ▼
                                          Azure Service Bus
                                                  │
                                                  ▼
                                           WhatsApp Worker
                                                  │
                                    ┌─────────────┴─────────────┐
                                    │                           │
                                    ▼                           ▼
                               Upload Media                 Send Message
                                    │                           │
                                    └─────────────┬─────────────┘
                                                  ▼
                                            Meta Graph API


Meta Webhooks
      │
      ▼
Webhook Endpoint
      │
      ▼
Inbox
      │
      ▼
Handlers
      │
      ├── Message Status
      ├── Template Status
      └── Phone/WABA Events
```

---

# 4. Estrutura recomendada da solução .NET

```text
Meta.WhatsApp.Client
Meta.WhatsApp.Api
Meta.WhatsApp.Application
Meta.WhatsApp.Domain
Meta.WhatsApp.Infrastructure
Meta.WhatsApp.Sdk
Meta.WhatsApp.Worker
WhatsApp.Tests
```

Dependências:

```text
Meta.WhatsApp.Api
    ↓
Meta.WhatsApp.Application
    ↓
Meta.WhatsApp.Domain

Meta.WhatsApp.Infrastructure
    ↓
Meta.WhatsApp.Application
    ↓
Meta.WhatsApp.Domain

Meta.WhatsApp.Worker
    ↓
Meta.WhatsApp.Application
    ↓
Meta.WhatsApp.Domain
```

O projeto `Domain` não deve depender de:

- EF Core;
- Azure SDK;
- Meta Graph API;
- Service Bus;
- Blob Storage;
- Key Vault.

---

# 5. Infraestrutura Azure

Provisionar:

```text
Azure Container Apps
Azure SQL Database
Azure Service Bus
Azure Blob Storage
Azure Key Vault
Application Insights
Log Analytics
```

Opcionalmente:

```text
Azure App Configuration
```

para feature flags e configurações dinâmicas.

---

# 6. Configuração inicial

Exemplo:

```json
{
  "Meta": {
    "GraphApiBaseUrl": "https://graph.facebook.com",
    "ApiVersion": "vXX.X"
  },
  "ServiceBus": {
    "Namespace": "..."
  },
  "Storage": {
    "Container": "whatsapp-media"
  }
}
```

Segredos devem ser recuperados por Managed Identity + Key Vault.

---

# 7. Modelo de domínio

## 7.1. Waba

```csharp
public sealed class Waba
{
    public Guid Id { get; init; }
    public string MetaWabaId { get; init; } = null!;
    public string? BusinessId { get; set; }
    public string? Name { get; set; }
    public string CredentialKey { get; init; } = null!;
    public WabaStatus Status { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
}
```

---

## 7.2. PhoneNumber

```csharp
public sealed class PhoneNumber
{
    public Guid Id { get; init; }
    public Guid WabaId { get; init; }
    public string MetaPhoneNumberId { get; init; } = null!;
    public string DisplayPhoneNumber { get; set; } = null!;
    public string? VerifiedName { get; set; }
    public string? QualityRating { get; set; }
    public string? PlatformType { get; set; }
    public string? Status { get; set; }
}
```

---

## 7.3. MessageTemplate

```csharp
public sealed class MessageTemplate
{
    public Guid Id { get; init; }
    public Guid WabaId { get; init; }
    public string? MetaTemplateId { get; set; }
    public string Name { get; init; } = null!;
    public string Language { get; init; } = null!;
    public string Category { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int CurrentVersion { get; set; }
}
```

---

## 7.4. MessageTemplateVersion

```csharp
public sealed class MessageTemplateVersion
{
    public Guid Id { get; init; }
    public Guid TemplateId { get; init; }
    public int Version { get; init; }
    public string ComponentsJson { get; init; } = null!;
    public string Hash { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
}
```

---

## 7.5. MessageDefinition

```csharp
public sealed class MessageDefinition
{
    public string Code { get; init; } = null!;
    public ContentStrategy ContentStrategy { get; init; }
    public string? TemplateName { get; init; }
    public string Language { get; init; } = "pt_BR";
    public string? Renderer { get; init; }
    public bool Enabled { get; init; }
}
```

```csharp
public enum ContentStrategy
{
    FreeText,
    TextTemplate,
    ImageTemplate
}
```

Exemplos:

```text
AVISO_SIMPLES
    FreeText

PROPOSTA_APROVADA
    TextTemplate

RESUMO_PROPOSTAS
    ImageTemplate

RESUMO_VEICULOS
    ImageTemplate
```

---

## 7.6. WhatsAppMessage

```csharp
public sealed class WhatsAppMessage
{
    public Guid Id { get; init; }
    public string IdempotencyKey { get; init; } = null!;
    public Guid WabaId { get; init; }
    public Guid PhoneNumberId { get; init; }
    public string Recipient { get; init; } = null!;
    public string MessageType { get; init; } = null!;
    public ContentStrategy ContentStrategy { get; init; }

    public string? TemplateName { get; set; }
    public string? MetaMessageId { get; set; }

    public WhatsAppMessageStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
```

Estados possíveis:

```text
CREATED
RENDERING
RENDERED
MEDIA_UPLOADING
MEDIA_READY
QUEUED
SENDING
SENT
DELIVERED
READ

RENDER_FAILED
MEDIA_UPLOAD_FAILED
SEND_FAILED
FAILED_PERMANENT
```

---

## 7.7. RenderedMedia

```csharp
public sealed class RenderedMedia
{
    public Guid Id { get; init; }
    public Guid MessageId { get; init; }

    public string StoragePath { get; init; } = null!;
    public string MimeType { get; init; } = null!;

    public string Sha256 { get; init; } = null!;

    public long Size { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    public string Renderer { get; init; } = null!;
    public string RendererVersion { get; init; } = null!;

    public string? MetaMediaId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
```

---

## 7.8. IntegrationOperation

```csharp
public sealed class IntegrationOperation
{
    public Guid Id { get; init; }
    public Guid WabaId { get; init; }

    public string OperationType { get; init; } = null!;
    public Guid? EntityId { get; init; }

    public string Status { get; set; } = null!;

    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
}
```

Tipos:

```text
CREATE_TEMPLATE
UPDATE_TEMPLATE
DELETE_TEMPLATE
SEND_TEMPLATE
SYNC_WABA
SYNC_TEMPLATE
UPLOAD_MEDIA
```

Estados:

```text
PENDING
PROCESSING
WAITING_META
SUCCEEDED
FAILED
FAILED_PERMANENT
```

---

# 8. Banco de dados

Criar as tabelas:

```text
whatsapp_waba
whatsapp_phone_number
whatsapp_template
whatsapp_template_version
whatsapp_message_definition
whatsapp_message
whatsapp_rendered_media
whatsapp_operation

integration_outbox
integration_inbox
```

Índices recomendados:

```text
whatsapp_waba
UNIQUE(meta_waba_id)

whatsapp_phone_number
UNIQUE(meta_phone_number_id)

whatsapp_template
UNIQUE(waba_id, name, language)

whatsapp_message
UNIQUE(idempotency_key)

integration_inbox
UNIQUE(message_id)

integration_outbox
INDEX(published_at, next_attempt_at)
```

Usar `rowversion` em agregados onde concorrência otimista for necessária.

---

# 9. Meta Graph API clients

Não criar um `IMetaService` monolítico.

Separar por responsabilidade:

```csharp
IWabaMetaClient
IPhoneNumberMetaClient
ITemplateMetaClient
IMediaMetaClient
IMessageMetaClient
```

Exemplo:

```csharp
public interface ITemplateMetaClient
{
    Task<IReadOnlyCollection<MetaTemplate>> GetTemplatesAsync(
        string wabaId,
        CancellationToken cancellationToken);

    Task<MetaTemplate> CreateAsync(
        string wabaId,
        CreateMetaTemplateRequest request,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        string templateId,
        UpdateMetaTemplateRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string wabaId,
        string templateName,
        CancellationToken cancellationToken);
}
```

---

# 10. Resiliência HTTP

Configurar `HttpClient` usando `Microsoft.Extensions.Http.Resilience` ou Resilience Pipelines.

Implementar:

```text
Timeout
Retry
Exponential Backoff
Jitter
Circuit Breaker
Rate Limiter
Telemetry
```

Erros tipicamente transitórios:

```text
408
429
5xx
```

Erros normalmente definitivos:

```text
400
401
403
404
422
```

Criar exceção padronizada:

```csharp
public sealed class MetaApiException : Exception
{
    public HttpStatusCode StatusCode { get; init; }

    public string? MetaErrorCode { get; init; }
    public string? MetaErrorSubCode { get; init; }
    public string? MetaTraceId { get; init; }

    public bool Retryable { get; init; }
}
```

---

# 11. Cadastro e sincronização da WABA

Endpoint:

```http
POST /api/wabas
```

Request:

```json
{
  "wabaId": "123456",
  "credentialKey": "meta-whatsapp-prod"
}
```

Fluxo:

```text
RegisterWabaUseCase
        │
        ▼
Azure Key Vault
        │
        ▼
recupera token
        │
        ▼
valida acesso à WABA
        │
        ├── GET /{WABA_ID}
        │
        ├── GET /{WABA_ID}/phone_numbers
        │
        └── GET /{WABA_ID}/message_templates
        │
        ▼
persistência
        │
        ├── WABA
        ├── PhoneNumbers
        ├── Templates
        └── Outbox
```

Todas as gravações devem acontecer em uma única transação SQL.

Implementar paginação para endpoints da Meta que retornem coleções.

---

# 12. Padrão Outbox

Estrutura sugerida:

```sql
CREATE TABLE integration_outbox
(
    id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    aggregate_type VARCHAR(100) NOT NULL,
    aggregate_id VARCHAR(200) NOT NULL,
    event_type VARCHAR(150) NOT NULL,

    payload NVARCHAR(MAX) NOT NULL,

    correlation_id UNIQUEIDENTIFIER NULL,
    causation_id UNIQUEIDENTIFIER NULL,

    occurred_at DATETIME2 NOT NULL,
    published_at DATETIME2 NULL,

    attempt_count INT NOT NULL DEFAULT 0,
    next_attempt_at DATETIME2 NULL,

    claimed_at DATETIME2 NULL,
    claimed_by VARCHAR(200) NULL,

    last_error NVARCHAR(MAX) NULL
);
```

Interface:

```csharp
public interface IOutboxRepository
{
    Task AddAsync(...);
    Task<IReadOnlyCollection<OutboxMessage>> ClaimPendingAsync(...);
    Task MarkPublishedAsync(...);
    Task RegisterFailureAsync(...);
}
```

---

# 13. Outbox Publisher Worker

Criar:

```text
OutboxPublisherWorker
```

Fluxo:

```text
SQL Outbox
    │
    ▼
claim batch
    │
    ▼
Azure Service Bus
    │
    ▼
MarkPublished
```

Começar com batches pequenos, por exemplo:

```text
50 mensagens
```

A implementação deve suportar múltiplas réplicas sem duas instâncias processarem o mesmo registro simultaneamente.

Garantia assumida:

```text
at-least-once delivery
```

Todos os consumidores precisam ser idempotentes.

---

# 14. Inbox

Estrutura:

```sql
CREATE TABLE integration_inbox
(
    message_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    message_type VARCHAR(150) NOT NULL,
    payload NVARCHAR(MAX) NULL,
    received_at DATETIME2 NOT NULL,
    processed_at DATETIME2 NULL
);
```

Fluxo:

```text
Service Bus Message
      │
      ▼
Inbox
      │
      ▼
message_id já existe?
   ┌──────┴──────┐
   │             │
  sim           não
   │             │
   ▼             ▼
ignore        processa
```

---

# 15. Gestão de templates

Endpoints:

```http
POST   /api/wabas/{wabaId}/templates
PUT    /api/wabas/{wabaId}/templates/{templateId}
DELETE /api/wabas/{wabaId}/templates/{templateId}

GET    /api/wabas/{wabaId}/templates
GET    /api/wabas/{wabaId}/templates/available
GET    /api/wabas/{wabaId}/templates/{templateId}
```

Operações de escrita devem ser assíncronas.

Exemplo:

```http
POST /api/wabas/{wabaId}/templates
```

Resposta:

```http
202 Accepted
```

```json
{
  "operationId": "84d74c..."
}
```

Fluxo:

```text
API
 │
 ▼
whatsapp_operation = PENDING
 │
 ▼
Outbox
 │
 ▼
Service Bus
 │
 ▼
Template Worker
 │
 ▼
Meta API
```

Handlers:

```text
CreateTemplateHandler
UpdateTemplateHandler
DeleteTemplateHandler
```

---

# 16. MessageDefinition

Definições iniciais podem ser feitas via migration ou seed.

Exemplo:

```text
AVISO_SIMPLES
ContentStrategy = FreeText

PROPOSTA_APROVADA
ContentStrategy = TextTemplate
TemplateName = proposta_aprovada
Language = pt_BR

RESUMO_PROPOSTAS
ContentStrategy = ImageTemplate
TemplateName = informativo_com_imagem
Language = pt_BR
Renderer = ProposalSummaryRenderer

RESUMO_VEICULOS
ContentStrategy = ImageTemplate
TemplateName = informativo_com_imagem
Language = pt_BR
Renderer = VehicleSummaryRenderer
```

Não criar painel administrativo no MVP.

---

# 17. API genérica de envio

Endpoint:

```http
POST /api/messages
```

Request:

```json
{
  "idempotencyKey": "proposta-123-aviso-1",
  "messageType": "RESUMO_PROPOSTAS",
  "recipient": "5527999999999",
  "context": {
    "wabaId": "123456",
    "phoneNumberId": "987654"
  },
  "data": {
    "propostas": []
  }
}
```

Resposta:

```http
202 Accepted
```

```json
{
  "messageId": "...",
  "status": "CREATED"
}
```

---

# 18. Estratégias de conteúdo

Criar:

```csharp
public interface IMessageStrategy
{
    ContentStrategy Strategy { get; }

    Task PrepareAsync(
        WhatsAppMessage message,
        MessageDefinition definition,
        JsonDocument data,
        CancellationToken cancellationToken);
}
```

Implementações:

```text
FreeTextMessageStrategy
TextTemplateMessageStrategy
ImageTemplateMessageStrategy
```

---

# 19. FreeText Strategy

Responsabilidade:

```text
MessageDefinition
      │
      ▼
FreeText
      │
      ▼
WhatsAppMessage
      │
      ▼
Outbox
      │
      ▼
MessageWorker
      │
      ▼
Meta
```

Deve ser aplicada apenas quando a política da Meta permitir texto livre.

O domínio deve ter uma abstração para verificar se há janela de conversa aplicável.

---

# 20. TextTemplate Strategy

Validações:

```text
WABA existe?
Phone pertence à WABA?
Template existe?
Template está APPROVED?
Language é compatível?
Parâmetros estão completos?
```

Após a validação:

```text
Message
   +
Outbox
```

---

# 21. Rendering Engine

Interface:

```csharp
public interface IMessageRenderer
{
    string Name { get; }

    Task<RenderedContent> RenderAsync(
        RenderContext context,
        CancellationToken cancellationToken);
}
```

Resultado:

```csharp
public sealed class RenderedContent
{
    public IReadOnlyCollection<RenderedPage> Pages { get; init; } = [];
}
```

```csharp
public sealed class RenderedPage
{
    public byte[] Content { get; init; } = [];
    public string MimeType { get; init; } = "image/png";
    public int Width { get; init; }
    public int Height { get; init; }
}
```

Implementações iniciais:

```text
ProposalSummaryRenderer
VehicleSummaryRenderer
AppointmentSummaryRenderer
```

Evitar criar um renderer de tabela excessivamente genérico logo no início.

---

# 22. Biblioteca visual

Criar um pequeno design system para as imagens.

Exemplo:

```text
WhatsAppImageTheme

Width
Padding
FontSize
HeaderHeight
RowHeight
Logo
Footer
MaxRows
```

Componentes reutilizáveis:

```text
Header
Title
Subtitle
Table
Badge
Footer
PaginationIndicator
```

---

# 23. Limitações do renderer

Definir explicitamente:

```text
MaxWidth
MaxHeight
MaxRowsPerPage
MaxFileSize
JPEGQuality
OutputFormat
```

Exemplo inicial:

```text
Width = 1080
MaxRowsPerPage = 10
OutputFormat = PNG
```

Se existirem 25 linhas:

```text
25 linhas
   │
   ├── página 1 = 10
   ├── página 2 = 10
   └── página 3 = 5
```

Mesmo que o MVP use uma única página, a estrutura do renderer deve suportar coleção de páginas.

---

# 24. Armazenamento das imagens

Salvar as imagens no Azure Blob Storage.

Path recomendado:

```text
whatsapp/
{yyyy}/
{MM}/
{dd}/
{messageId}/
{page}.png
```

Não persistir binários ou Base64 na Outbox.

Persistir apenas referências:

```json
{
  "mediaId": "...",
  "storagePath": "whatsapp/2026/08/24/abc/1.png",
  "contentType": "image/png",
  "sha256": "..."
}
```

---

# 25. Cache de renderização

Calcular:

```text
SHA256(
    rendererVersion
    +
    normalizedData
)
```

Fluxo:

```text
dados
  │
  ▼
normalização
  │
  ▼
SHA256
  │
  ▼
já existe mídia?
 ┌───────┴───────┐
 │               │
sim             não
 │               │
 ▼               ▼
reuse         renderiza
```

Adicionar:

```text
renderer_version
```

Exemplo:

```text
ProposalSummaryRenderer-v3
```

Qualquer alteração visual pode incrementar a versão e invalidar o cache automaticamente.

---

# 26. ImageTemplate Strategy

Fluxo:

```text
RESUMO_PROPOSTAS
       │
       ▼
MessageDefinition
       │
       ▼
ImageTemplate
       │
       ▼
ProposalSummaryRenderer
       │
       ▼
PNG
       │
       ▼
Azure Blob Storage
       │
       ▼
RenderedMedia
       │
       ▼
Outbox
```

Depois:

```text
Message Worker
      │
      ▼
Blob
      │
      ▼
Meta Media Upload
      │
      ▼
MetaMediaId
      │
      ▼
Template Send
```

---

# 27. Meta Media Uploader

Criar:

```csharp
public interface IMetaMediaUploader
{
    Task<string> UploadAsync(
        string phoneNumberId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);
}
```

Fluxo:

```text
RenderedMedia
    │
    ▼
Blob Storage
    │
    ▼
POST /{PHONE_NUMBER_ID}/media
    │
    ▼
MetaMediaId
```

Persistir:

```text
meta_media_id
uploaded_at
```

O Blob Storage permanece como fonte confiável da mídia.

---

# 28. Message Worker

Recebe:

```text
WhatsAppMessageSendRequested
```

Fluxo:

```text
Message
   │
   ▼
ContentStrategy
   │
   ├── FreeText
   │      └── send text
   │
   ├── TextTemplate
   │      └── send template
   │
   └── ImageTemplate
          │
          ├── garantir MetaMediaId
          └── send template + image
```

Após o envio:

```text
MetaMessageId
SentAt
Status = SENT
```

---

# 29. Templates genéricos de mídia

Evitar criar um template Meta para cada tipo de tabela.

Preferir templates genéricos, por exemplo:

```text
informativo_com_imagem
```

Estrutura:

```text
HEADER
  IMAGE

BODY
  {{1}}

FOOTER
  Esta é uma mensagem automática.
```

Então o mesmo template pode ser usado com:

```text
propostas.png
veiculos.png
agenda.png
parcelas.png
```

A diferenciação pertence à `MessageDefinition`.

---

# 30. Webhook Meta

Endpoints:

```http
GET  /api/meta/webhook
POST /api/meta/webhook
```

O endpoint deve executar o mínimo possível.

Fluxo:

```text
Meta
 │
 ▼
WebhookController
 │
 ├── valida assinatura
 ├── persiste Inbox
 └── responde rapidamente
```

Não colocar regras de negócio complexas dentro da requisição HTTP do webhook.

---

# 31. Handlers de webhook

Criar handlers para:

```text
MessageStatusChanged
TemplateStatusChanged
PhoneStatusChanged
```

Estados de mensagem:

```text
SENT
DELIVERED
READ
FAILED
```

Estados de template podem incluir:

```text
PENDING
APPROVED
REJECTED
DISABLED
FLAGGED
DELETED
```

Fluxo:

```text
Webhook
   │
   ▼
Inbox
   │
   ▼
Handler
   │
   ▼
Atualiza domínio
   │
   ▼
Outbox
```

---

# 32. Reconciliação

Criar:

```text
MetaReconciliationWorker
```

Executado periodicamente.

Para cada WABA:

```text
GET /{WABA_ID}/phone_numbers
GET /{WABA_ID}/message_templates
```

Comparar:

```text
Estado Meta
     VS
Estado local
```

Corrigir divergências.

Exemplo:

```text
Local = PENDING
Meta  = APPROVED

       ↓

Atualiza local

       ↓

TemplateStatusChanged
```

Isso protege contra:

- webhook perdido;
- indisponibilidade;
- deploy;
- alteração manual no WhatsApp Manager;
- alteração feita por outro sistema;
- falhas temporárias.

---

# 33. Retry

Separar dois tipos de retry.

## 33.1. Retry HTTP curto

Exemplo:

```text
2 segundos
5 segundos
10 segundos
```

Aplicado a:

```text
408
429
5xx
```

---

## 33.2. Retry operacional

Persistir:

```text
attempt_count
next_attempt_at
last_error
```

Exemplo:

```text
1 minuto
5 minutos
15 minutos
1 hora
```

Não manter threads aguardando.

---

# 34. Circuit Breaker

Aplicar circuit breaker na integração com a Meta.

Objetivo:

```text
Meta indisponível
      │
      ▼
várias falhas
      │
      ▼
circuit OPEN
      │
      ▼
falha rápida
      │
      ▼
tentativa posterior
```

Evita desperdício de:

- threads;
- conexões;
- sockets;
- CPU;
- quota.

---

# 35. Rate Limiting

Aplicar controle de concorrência por:

```text
WABA
```

e, quando necessário:

```text
PhoneNumber
```

Exemplo:

```text
Queue
 │
 ├── WABA A → limite próprio
 ├── WABA B → limite próprio
 └── WABA C → limite próprio
```

Não permitir que múltiplos workers produzam uma tempestade de `429`.

---

# 36. Idempotência

Todo comando externo deve possuir:

```text
idempotencyKey
```

Exemplo:

```json
{
  "idempotencyKey": "aviso-venda-193881-whatsapp"
}
```

Criar:

```text
UNIQUE(idempotency_key)
```

A solução deve assumir duplicidade em:

```text
HTTP retries
Outbox retries
Service Bus redelivery
Worker retry
Webhook redelivery
```

---

# 37. DLQ

Depois do limite de tentativas:

```text
FAILED_PERMANENT
```

e encaminhar a mensagem para DLQ.

Registrar:

```text
message_id
operation_id
waba_id
phone_number_id
template_name
http_status
meta_error_code
meta_error_subcode
meta_trace_id
attempts
last_error
payload_reference
timestamp
```

Criar API operacional:

```http
GET /api/operations/{id}
```

Futuramente:

```http
POST /api/operations/{id}/retry
```

---

# 38. Observabilidade

Propagar:

```text
CorrelationId
CausationId
MessageId
OperationId
WabaId
PhoneNumberId
TemplateName
MetaMessageId
MetaTraceId
```

Fluxo:

```text
Sistema consumidor
      │
      ▼
CorrelationId ABC
      │
      ▼
WhatsApp API
      │
      ▼
Outbox
      │
      ▼
Service Bus
      │
      ▼
Worker
      │
      ▼
Meta
```

Métricas:

```text
messages_requested
messages_sent
messages_failed
messages_delivered
messages_read

media_render_duration
media_upload_duration

meta_api_duration
meta_api_429
meta_api_errors

outbox_pending
outbox_oldest_age

inbox_duplicates

dlq_count

template_pending
template_rejected
```

---

# 39. Alertas

Criar alertas para:

```text
DLQ > 0

FailureRate > limite

Meta 429 acima do limite

OutboxAge > 5 minutos

Circuit breaker OPEN

Webhook sem receber eventos

TemplateRejected > 0

RenderFailure > 0
```

---

# 40. Health checks

Endpoint:

```http
GET /health
```

Verificar:

```text
Azure SQL
Azure Service Bus
Azure Blob Storage
Azure Key Vault
```

Não usar chamada à Meta como readiness check síncrono obrigatório.

---

# 41. Testes

## Unit tests

Cobrir:

```text
MessageDefinition selection
ContentStrategy selection
Template validation
Renderer
Pagination
Hash generation
Idempotency
Retry classification
State transitions
```

---

## Integration tests

Usar:

```text
SQL real/container
Blob Storage emulator ou test account
Service Bus test namespace
Meta API mock
```

---

## Contract tests

Cobrir:

```text
Meta DTOs
Webhook payloads
Message payloads
Template payloads
Media payloads
```

---

## End-to-end

Fluxo mínimo:

```text
POST /api/messages
      │
      ▼
Outbox
      │
      ▼
Service Bus
      │
      ▼
Worker
      │
      ▼
Meta Mock
      │
      ▼
Webhook
      │
      ▼
DELIVERED
```

---

# 42. Sequência de implementação

## Etapa 1 — Fundação

- criar solution e projetos;
- configurar DI;
- configurar logging;
- configurar health checks;
- configurar CI/CD;
- provisionar infraestrutura Azure;
- configurar Managed Identity;
- configurar Key Vault.

---

## Etapa 2 — Domínio e persistência

- criar entidades;
- criar enums;
- criar DbContext;
- criar migrations;
- criar repositories;
- criar índices;
- configurar `rowversion`.

---

## Etapa 3 — Meta Clients

- `IWabaMetaClient`;
- `IPhoneNumberMetaClient`;
- `ITemplateMetaClient`;
- `IMediaMetaClient`;
- `IMessageMetaClient`;
- DTOs;
- error mapping;
- autenticação.

---

## Etapa 4 — Resiliência HTTP

- timeout;
- retry;
- exponential backoff;
- jitter;
- circuit breaker;
- rate limiting;
- telemetria.

---

## Etapa 5 — Cadastro/sync de WABA

- `POST /api/wabas`;
- validação da WABA;
- Key Vault;
- recuperação de números;
- recuperação de templates;
- paginação;
- persistência;
- eventos de sincronização.

---

## Etapa 6 — Outbox

- tabela;
- repository;
- claim;
- publication state;
- failure state;
- retry state.

---

## Etapa 7 — Service Bus

- topics/queues;
- contratos de eventos;
- publisher;
- worker.

---

## Etapa 8 — Inbox

- tabela;
- repository;
- deduplicação;
- middleware/handler base.

---

## Etapa 9 — Template Management

- criar template;
- atualizar template;
- excluir template;
- consultar template;
- operation tracking;
- handlers;
- sync de status.

---

## Etapa 10 — MessageDefinition

- tabela;
- seed;
- lookup;
- validação;
- strategy resolution.

---

## Etapa 11 — FreeText

- strategy;
- validação;
- mensagem;
- Outbox;
- envio;
- status.

---

## Etapa 12 — TextTemplate

- strategy;
- template lookup;
- status `APPROVED`;
- parameter binding;
- envio.

---

## Etapa 13 — Rendering Engine

- interfaces;
- render context;
- rendered pages;
- design system;
- `ProposalSummaryRenderer`;
- testes de layout.

---

## Etapa 14 — Blob Storage

- upload;
- leitura;
- cleanup;
- path strategy;
- metadata.

---

## Etapa 15 — Cache de renderização

- normalização;
- hash;
- renderer version;
- lookup por hash;
- reuse.

---

## Etapa 16 — ImageTemplate

- strategy;
- renderer;
- Blob;
- media entity;
- Outbox.

---

## Etapa 17 — Meta Media Upload

- upload da imagem;
- `MetaMediaId`;
- retry;
- persistence.

---

## Etapa 18 — Message Worker

- FreeText;
- TextTemplate;
- ImageTemplate;
- transition states;
- failure handling.

---

## Etapa 19 — Webhook

- verification;
- signature validation;
- Inbox;
- fast response.

---

## Etapa 20 — Webhook Handlers

- message status;
- template status;
- WABA/phone events;
- Outbox.

---

## Etapa 21 — Reconciliação

- worker periódico;
- phone sync;
- template sync;
- divergence detection;
- corrective updates.

---

## Etapa 22 — DLQ / Retry

- operational retry;
- max attempts;
- DLQ;
- operation status;
- replay endpoint futuro.

---

## Etapa 23 — Observabilidade

- structured logs;
- tracing;
- dashboards;
- metrics;
- alerts.

---

## Etapa 24 — Hardening

- carga;
- rate limiting;
- chaos/failure testing;
- credential rotation;
- cleanup de blobs;
- retenção;
- security review.

---

# 43. Milestones

## Milestone 1 — Meta Management

Entregar:

```text
WABA
Phone Numbers
Templates
Sync
Key Vault
Meta Clients
```

Resultado:

> O serviço conhece e sincroniza os ativos da WABA.

---

## Milestone 2 — Mensageria simples

Entregar:

```text
MessageDefinition
FreeText
TextTemplate
Outbox
Service Bus
Workers
```

Resultado:

> O serviço já consegue enviar mensagens reais.

---

## Milestone 3 — Conteúdo estruturado

Entregar:

```text
Renderer
PNG/JPG
Blob Storage
ImageTemplate
Media Upload
Pagination
Render Cache
```

Resultado:

> O serviço consegue transformar estruturas tabulares em imagens e enviá-las sem expor essa decisão aos consumidores.

---

## Milestone 4 — Resiliência completa

Entregar:

```text
Inbox
Idempotency
Retry
DLQ
Circuit Breaker
Rate Limiting
Reconciliation
```

Resultado:

> O serviço está preparado para falhas transitórias e duplicidades.

---

## Milestone 5 — Operação

Entregar:

```text
Application Insights
Dashboards
Alerts
Tracing
Operation API
Replay
```

Resultado:

> O time de suporte consegue operar e diagnosticar a integração.

---

# 44. Épicos sugeridos

```text
EP01 — Fundação do WhatsApp Messaging Service

EP02 — Integração e sincronização Meta/WABA

EP03 — Gerenciamento de templates

EP04 — Infraestrutura Outbox/Inbox

EP05 — Mensageria WhatsApp

EP06 — Renderização de conteúdo estruturado

EP07 — Gerenciamento de mídia

EP08 — Webhooks Meta

EP09 — Resiliência e idempotência

EP10 — Observabilidade e operação
```

---

# 45. Contrato de negócio recomendado

Princípio:

```text
Sistema consumidor
        │
        ▼
 intenção de negócio
        │
        ▼
WhatsApp Messaging Service
        │
        ├── resolve MessageDefinition
        ├── decide FreeText / Template / Image
        ├── seleciona template
        ├── renderiza imagem quando necessário
        ├── gerencia mídia
        ├── aplica regras da Meta
        ├── garante idempotência
        └── entrega mensagem
```

Nenhum sistema consumidor deve precisar conhecer:

```text
MetaWabaId interno
MetaTemplateId
MetaMediaId
Graph API
PNG/JPG
limitações de formatação da Meta
detalhes de retry
webhooks
```

Esses conceitos devem permanecer encapsulados dentro do microsserviço.

---

# 46. Critérios de aceite finais

A implementação pode ser considerada completa quando:

- uma WABA puder ser cadastrada e validada;
- números puderem ser sincronizados;
- templates puderem ser sincronizados;
- templates puderem ser criados, atualizados e excluídos;
- alterações de status de template forem refletidas via webhook;
- texto livre puder ser enviado quando permitido;
- templates de texto puderem ser enviados;
- conteúdos estruturados puderem ser transformados em PNG/JPG;
- imagens forem armazenadas no Blob Storage;
- imagens puderem ser enviadas à Meta e utilizadas em templates;
- todas as operações forem idempotentes;
- eventos utilizarem Outbox;
- consumidores utilizarem Inbox;
- retries não produzirem duplicidade;
- erros definitivos forem encaminhados para DLQ;
- rate limiting evitar tempestades de 429;
- reconciliação corrigir divergências;
- logs permitirem rastrear uma mensagem do consumidor até a Meta;
- dashboards e alertas estiverem configurados;
- o token da Meta não estiver presente em banco, logs, eventos ou arquivos de configuração.
