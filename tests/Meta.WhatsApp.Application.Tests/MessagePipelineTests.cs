using System.Text.Json;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Tests.Support;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Application.Webhooks;
using Meta.WhatsApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Meta.WhatsApp.Application.Tests;

public sealed class MessagePipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FreeTextMessageIsSentOnceAndRepeatedProcessingIsIdempotent()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        using var payload = JsonDocument.Parse("{\"text\":\"Olá\",\"previewUrl\":true}");
        var requested = await QueueAsync(database, time, "AVISO_SIMPLES", payload.RootElement.Clone(), "free:1");
        var processor = CreateProcessor(database, meta, new InMemoryBlobStorage(), new FakeRenderer(), time);

        var sent = await processor.ProcessAsync(requested, CancellationToken.None);
        var duplicate = await processor.ProcessAsync(requested, CancellationToken.None);

        Assert.Equal(WhatsAppMessageStatus.Sent, sent.Status);
        Assert.True(duplicate.AlreadyProcessed);
        Assert.Single(meta.SentMessages);
        Assert.Equal("text", meta.SentMessages[0].Type);
        Assert.Single(database.DbContext.Outbox.Where(item =>
            item.EventType == IntegrationEventTypes.MessageStatusChanged));
    }

    [Fact]
    public async Task ImageTemplateRendersStoresUploadsAndSendsMediaOnlyOnce()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients
        {
            Templates = [FakeMetaClients.ApprovedTemplate("template-image", "resumo_propostas")]
        };
        await RegisterAsync(database, meta, time);
        using var payload = JsonDocument.Parse(
            "{\"propostas\":[{\"cliente\":\"Ana\",\"produto\":\"Plano\",\"valor\":100,\"status\":\"Aprovada\"}]}");
        var requested = await QueueAsync(database, time, "RESUMO_PROPOSTAS", payload.RootElement.Clone(), "image:1");
        var blob = new InMemoryBlobStorage();
        var renderer = new FakeRenderer();
        var processor = CreateProcessor(database, meta, blob, renderer, time);

        var first = await processor.ProcessAsync(requested, CancellationToken.None);
        var duplicate = await processor.ProcessAsync(requested, CancellationToken.None);

        Assert.Equal(WhatsAppMessageStatus.Sent, first.Status);
        Assert.True(duplicate.AlreadyProcessed);
        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(1, blob.UploadCount);
        Assert.Equal(1, meta.UploadCount);
        Assert.Single(meta.SentMessages);
        Assert.Equal("template", meta.SentMessages[0].Type);
        Assert.Single(database.DbContext.RenderedMedia);
    }

    [Fact]
    public async Task TransientRateLimitSchedulesRetryAndNextAttemptCompletes()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        using var payload = JsonDocument.Parse("{\"text\":\"Tente novamente\"}");
        var requested = await QueueAsync(database, time, "AVISO_SIMPLES", payload.RootElement.Clone(), "retry:1");
        var processor = CreateProcessor(database, meta, new InMemoryBlobStorage(), new FakeRenderer(), time);
        meta.FailNextSend();

        var failure = await Assert.ThrowsAsync<MessageProcessingException>(() =>
            processor.ProcessAsync(requested, CancellationToken.None));
        time.Advance(TimeSpan.FromSeconds(30));
        var recovered = await processor.ProcessAsync(requested, CancellationToken.None);
        var operation = await database.DbContext.Operations.SingleAsync(
            item => item.Id == requested.OperationId,
            CancellationToken.None);

        Assert.Equal(Now.AddSeconds(30), failure.RetryAt);
        Assert.Equal(WhatsAppMessageStatus.Sent, recovered.Status);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal(2, operation.Attempts);
        Assert.Single(meta.SentMessages);
    }

    [Fact]
    public async Task InvalidPayloadFailsPermanentlyWithoutCallingMeta()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        using var payload = JsonDocument.Parse("{\"unexpected\":true}");
        var requested = await QueueAsync(database, time, "AVISO_SIMPLES", payload.RootElement.Clone(), "invalid:1");
        var processor = CreateProcessor(database, meta, new InMemoryBlobStorage(), new FakeRenderer(), time);

        var result = await processor.ProcessAsync(requested, CancellationToken.None);
        var operation = await database.DbContext.Operations.SingleAsync(
            item => item.Id == requested.OperationId,
            CancellationToken.None);

        Assert.Equal(WhatsAppMessageStatus.FailedPermanent, result.Status);
        Assert.Equal(OperationStatus.FailedPermanent, operation.Status);
        Assert.Empty(meta.SentMessages);
    }

    [Fact]
    public async Task RetryLimitConvertsRepeatedTransientFailuresIntoPermanentFailure()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        using var payload = JsonDocument.Parse("{\"text\":\"Sempre limitado\"}");
        var requested = await QueueAsync(database, time, "AVISO_SIMPLES", payload.RootElement.Clone(), "retry:max");
        var processor = CreateProcessor(database, meta, new InMemoryBlobStorage(), new FakeRenderer(), time);

        for (var attempt = 1; attempt <= 9; attempt++)
        {
            meta.FailNextSend();
            var failure = await Assert.ThrowsAsync<MessageProcessingException>(() =>
                processor.ProcessAsync(requested, CancellationToken.None));
            time.SetUtcNow(failure.RetryAt);
        }

        meta.FailNextSend();
        var final = await processor.ProcessAsync(requested, CancellationToken.None);
        var operation = await database.DbContext.Operations.SingleAsync(
            item => item.Id == requested.OperationId,
            CancellationToken.None);

        Assert.Equal(WhatsAppMessageStatus.FailedPermanent, final.Status);
        Assert.Equal(OperationStatus.FailedPermanent, operation.Status);
        Assert.Equal(10, operation.Attempts);
        Assert.Empty(meta.SentMessages);
    }

    [Fact]
    public async Task DuplicateWebhooksAreIgnoredAndDeliveryNeverRegresses()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        using var payload = JsonDocument.Parse("{\"text\":\"Acompanhar\"}");
        var requested = await QueueAsync(database, time, "AVISO_SIMPLES", payload.RootElement.Clone(), "webhook:1");
        var processor = CreateProcessor(database, meta, new InMemoryBlobStorage(), new FakeRenderer(), time);
        await processor.ProcessAsync(requested, CancellationToken.None);
        var ingestion = new WebhookIngestionService(database.WebhookStore, time);
        var deliveredAt = Now.AddMinutes(1);
        var delivered = new IncomingWebhookEvent(
            "status",
            "phone-1",
            "wamid-1",
            "5511999990000",
            "delivered",
            deliveredAt,
            "{}");

        var firstIngestion = await ingestion.IngestAsync([delivered], Guid.NewGuid(), CancellationToken.None);
        var duplicateIngestion = await ingestion.IngestAsync([delivered], Guid.NewGuid(), CancellationToken.None);
        var deliveredEvent = await GetLastWebhookEventAsync(database);
        var webhookProcessor = new WebhookEventProcessor(
            database.Inbox,
            database.Messages,
            database.Outbox,
            database.UnitOfWork,
            time);
        var processed = await webhookProcessor.ProcessAsync(deliveredEvent, Guid.NewGuid(), CancellationToken.None);
        var duplicateProcessing = await webhookProcessor.ProcessAsync(deliveredEvent, Guid.NewGuid(), CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(2));
        var read = delivered with { Status = "read", OccurredAt = Now.AddMinutes(2) };
        await ingestion.IngestAsync([read], Guid.NewGuid(), CancellationToken.None);
        var readEvent = await GetLastWebhookEventAsync(database);
        await webhookProcessor.ProcessAsync(readEvent, Guid.NewGuid(), CancellationToken.None);
        var message = await database.DbContext.Messages.SingleAsync(CancellationToken.None);

        Assert.Equal(1, firstIngestion.Accepted);
        Assert.Equal(1, duplicateIngestion.Duplicates);
        Assert.True(processed.MessageMatched);
        Assert.False(duplicateProcessing.Processed);
        Assert.Equal(WhatsAppMessageStatus.Read, message.Status);
        Assert.Equal(deliveredAt, message.DeliveredAt);
        Assert.Equal(Now.AddMinutes(2), message.ReadAt);
    }

    private static MessageProcessor CreateProcessor(
        TestDatabase database,
        FakeMetaClients meta,
        InMemoryBlobStorage blob,
        FakeRenderer renderer,
        TimeProvider timeProvider) => new(
        database.Wabas,
        database.PhoneNumbers,
        database.Definitions,
        database.Messages,
        database.Media,
        database.Operations,
        database.Outbox,
        database.UnitOfWork,
        meta,
        meta,
        blob,
        renderer,
        new MessageContentStrategyResolver(
        [
            new FreeTextContentStrategy(),
            new TextTemplateContentStrategy(),
            new ImageTemplateContentStrategy()
        ]),
        timeProvider);

    private static async Task<MessageRequestedEvent> QueueAsync(
        TestDatabase database,
        TimeProvider timeProvider,
        string messageType,
        JsonElement payload,
        string idempotencyKey)
    {
        var handler = new SendNotificationHandler(
            database.Wabas,
            database.PhoneNumbers,
            database.Definitions,
            database.Templates,
            database.Messages,
            database.Operations,
            database.Outbox,
            database.UnitOfWork,
            timeProvider);
        var result = await handler.HandleAsync(
            new SendNotificationCommand(
                "waba-123",
                "phone-1",
                messageType,
                "5511999990000",
                payload,
                idempotencyKey),
            CancellationToken.None);
        var operation = await database.DbContext.Operations.SingleAsync(
            item => item.EntityId == result.MessageId,
            CancellationToken.None);
        return new MessageRequestedEvent(result.MessageId, operation.Id);
    }

    private static async Task RegisterAsync(
        TestDatabase database,
        FakeMetaClients meta,
        TimeProvider timeProvider)
    {
        var handler = new RegisterWabaHandler(
            meta,
            meta,
            meta,
            database.Wabas,
            database.PhoneNumbers,
            database.Templates,
            database.Outbox,
            database.UnitOfWork,
            timeProvider);
        await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None);
    }

    private static async Task<WebhookReceivedEvent> GetLastWebhookEventAsync(TestDatabase database)
    {
        var outbox = await database.DbContext.Outbox
            .Where(item => item.EventType == IntegrationEventTypes.WebhookReceived)
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .FirstAsync(CancellationToken.None);
        return JsonSerializer.Deserialize<WebhookReceivedEvent>(outbox.Payload, ApplicationJson.SerializerOptions)!;
    }
}
