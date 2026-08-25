using Meta.WhatsApp.Sessions;

namespace Meta.WhatsApp.Webhooks;

/// <summary>Connects authenticated webhook notifications to a configured Meta client.</summary>
public static class MetaWebhookProcessingExtensions
{
    public static async Task<MetaWebhookProcessingResult> ProcessWebhookAsync(
        this IMetaWhatsAppClient client,
        ReadOnlyMemory<byte> payload,
        string? signatureHeader,
        string appSecret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        MetaWebhookSignatureValidator.EnsureValid(payload.Span, signatureHeader, appSecret);
        return await client
            .ProcessWebhookAsync(MetaWebhookParser.Parse(payload), cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<MetaWebhookProcessingResult> ProcessWebhookAsync(
        this IMetaWhatsAppClient client,
        MetaWebhookNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(notification);

        var inboundRegistrations = new List<InboundRegistrationResult>();
        var statusUpdates = new List<ConversationSession?>();
        var ignored = 0;

        foreach (var message in notification.InboundMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(message.ChannelId, client.ChannelId, StringComparison.Ordinal))
            {
                ignored++;
                continue;
            }

            inboundRegistrations.Add(await client
                .RegisterInboundMessageWithResultAsync(message.ToInboundMessage(), cancellationToken)
                .ConfigureAwait(false));
        }

        foreach (var status in notification.StatusUpdates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(status.ChannelId, client.ChannelId, StringComparison.Ordinal) ||
                !status.TryCreateReengagementStatusUpdate(out var update))
            {
                ignored++;
                continue;
            }

            statusUpdates.Add(await client
                .RegisterReengagementStatusAsync(update!, cancellationToken)
                .ConfigureAwait(false));
        }

        return new MetaWebhookProcessingResult(inboundRegistrations, statusUpdates, ignored);
    }
}
