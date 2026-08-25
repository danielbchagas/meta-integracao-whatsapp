# Matriz de testes funcionais

Esta matriz registra os cenários determinísticos cobertos pela solução e as fronteiras que dependem de recursos Azure reais. A cobertura considera caminhos de sucesso, validação, recurso inexistente, duplicidade, estado inválido, falha transitória e falha definitiva.

## Suítes

| Projeto | Escopo | Casos executados |
|---|---|---:|
| `Meta.WhatsApp.Client.Tests` | SDK cliente, contratos HTTP, sessões, templates, mensagens e webhooks | 54 |
| `Meta.WhatsApp.Client.Acceptance.Tests` | Especificações ReqNRoll do SDK em português | 55 |
| `Meta.WhatsApp.Domain.Tests` | Invariantes e transições de estado | 8 |
| `Meta.WhatsApp.Application.Tests` | Casos de uso, estratégias e pipelines completos | 25 |
| `Meta.WhatsApp.Infrastructure.Tests` | EF Core, contratos Meta e renderizadores | 15 |
| `Meta.WhatsApp.Sdk.Tests` | Fachada pública e composição das camadas | 2 |
| `Meta.WhatsApp.Api.Tests` | Contratos HTTP e fluxos funcionais da API | 7 |
| `Meta.WhatsApp.Worker.Tests` | Publicação da outbox e despacho de eventos | 7 |
| **Total** | | **173** |

## Cenários cobertos

| Área | Cenários |
|---|---|
| WABA | cadastro e validação remota; consulta; sincronização idempotente; atualização; remoção local de telefones/templates ausentes; rejeição de identificador divergente; recurso inexistente |
| Templates | paginação Meta; consulta; filtro de disponíveis (`APPROVED`); mapeamento para tipos habilitados; criação, atualização, versionamento e exclusão assíncronos; chave natural duplicada; falhas transitória e definitiva |
| Mensagens | seleção de definição e estratégia; texto livre; template textual; template com imagem; validação de template aprovado; idempotência por chave; consulta; payload inválido |
| Renderização e mídia | canonicalização/hash; PNG de propostas e veículos; lista vazia; payload inválido; cache local; Blob; upload de mídia; reutilização sem duplicar envio |
| Estado e resiliência | transições válidas/inválidas; normalização do destinatário; classificação de 429/5xx e 4xx; `Retry-After`; recuperação; limite de dez tentativas; falha permanente |
| Meta Graph API | autenticação Bearer; DTOs de WABA/telefone/template; cursores repetidos; criar/editar/excluir template; multipart de mídia; envelope de mensagem; erro estruturado; JSON inválido |
| Webhooks | challenge válido/inválido; HMAC válido/inválido; payload malformado; inbox/outbox atômicos; deduplicação; inbound; `delivered`; `read`; processamento repetido sem regressão |
| Outbox/Worker | seleção por agenda; claim; timeout/reclaim; publicação; marcação de sucesso; falha e reagendamento exponencial; evento terminal; evento desconhecido/malformado |
| API | WABA, telefones, templates, templates disponíveis, operações, mensagens, idempotência, erros 400/401/403/404, webhooks e health checks |
| SDK cliente | janela de 24 horas; concorrência; tipos de mensagem; reengajamento; cooldown; correlação; status fora de ordem; segurança e deduplicação de webhooks |

## Fronteiras externas

Os testes locais não acessam contas reais e não armazenam segredos. SQL Server, Blob Storage, Service Bus, Key Vault e Meta são exercitados por modelo SQL gerado, banco EF InMemory e doubles de contrato.

Os seguintes testes exigem um ambiente de integração provisionado e devem ser executados no pipeline de infraestrutura com credenciais federadas:

- disputa real de locks `UPDLOCK/READPAST` no Azure SQL;
- entrega, abandono e DLQ reais no Azure Service Bus;
- autenticação por Managed Identity e rotação no Key Vault;
- upload, metadados, expiração e leitura no Azure Blob Storage;
- smoke test em uma WABA de homologação da Meta.

Essas fronteiras não impedem a suíte local de validar todos os ramos funcionais determinísticos do código.

## Execução

```powershell
dotnet test MetaIntegracaoWhatsApp.slnx
```
