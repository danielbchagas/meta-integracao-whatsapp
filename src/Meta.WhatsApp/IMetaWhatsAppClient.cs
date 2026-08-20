using Meta.WhatsApp.Messages;
using Meta.WhatsApp.Sessions;
using Meta.WhatsApp.Templates;

namespace Meta.WhatsApp;

/// <summary>Client for sending WhatsApp Cloud API messages and managing message templates.</summary>
public interface IMetaWhatsAppClient
{
    Task<SendMessageResult> SendMessageAsync(
        OutboundMessage message,
        CancellationToken cancellationToken = default);

    Task<SendMessageResult> SendTextMessageAsync(
        string recipient,
        string body,
        bool previewUrl = false,
        CancellationToken cancellationToken = default);

    Task<SendMessageResult> SendTemplateMessageAsync(
        string recipient,
        string templateName,
        string languageCode,
        IReadOnlyList<TemplateMessageComponent>? components = null,
        CancellationToken cancellationToken = default);

    Task<ConversationSession> RegisterInboundMessageAsync(
        InboundMessage message,
        CancellationToken cancellationToken = default);

    Task<ConversationSession?> GetOpenSessionAsync(
        string recipient,
        CancellationToken cancellationToken = default);

    Task<ConversationSession?> GetSessionAsync(
        string recipient,
        CancellationToken cancellationToken = default);

    Task CloseSessionAsync(
        string recipient,
        CancellationToken cancellationToken = default);

    Task<ReengagementResult> ReengageAsync(
        ReengagementRequest request,
        CancellationToken cancellationToken = default);

    Task<ConversationSession?> RegisterReengagementStatusAsync(
        ReengagementStatusUpdate update,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WhatsAppTemplate>> GetTemplatesAsync(
        string? name = null,
        CancellationToken cancellationToken = default);

    Task<WhatsAppTemplate?> GetTemplateByIdAsync(
        string templateId,
        CancellationToken cancellationToken = default);

    Task<CreateTemplateResult> CreateTemplateAsync(
        TemplateDefinition definition,
        CancellationToken cancellationToken = default);

    Task UpdateTemplateAsync(
        string templateId,
        TemplateDefinition definition,
        CancellationToken cancellationToken = default);

    Task<TemplateSynchronizationResult> EnsureTemplateAsync(
        TemplateDefinition definition,
        CancellationToken cancellationToken = default);
}
