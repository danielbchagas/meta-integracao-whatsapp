using System.Text.Json;
using Meta.WhatsApp.Application;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Tests.Support;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Meta.WhatsApp.Application.Tests;

public sealed class NotificationAndTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendNotificationQueuesMessageOperationAndOutboxAndDeduplicatesRetry()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        var handler = CreateSendHandler(database, time);
        using var payload = JsonDocument.Parse("{\"text\":\"Olá\"}");
        var command = new SendNotificationCommand(
            "waba-123",
            "phone-1",
            "AVISO_SIMPLES",
            "+55 11 99999-0000",
            payload.RootElement.Clone(),
            "pedido:123",
            Guid.NewGuid());

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var duplicate = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(first.Duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(first.MessageId, duplicate.MessageId);
        Assert.Single(database.DbContext.Messages);
        Assert.Single(database.DbContext.Operations.Where(item => item.OperationType == OperationType.SendMessage));
        Assert.Single(database.DbContext.Outbox.Where(item => item.EventType == IntegrationEventTypes.MessageRequested));
    }

    [Fact]
    public async Task TemplateMessageRequiresApprovedLocalTemplate()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        var handler = CreateSendHandler(database, time);
        using var payload = JsonDocument.Parse("{\"parameters\":[\"Daniel\"]}");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new SendNotificationCommand(
                "waba-123",
                "phone-1",
                "PROPOSTA_APROVADA",
                "5511999990000",
                payload.RootElement.Clone(),
                "proposal:1"),
            CancellationToken.None));

        Assert.Contains("not approved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(database.DbContext.Messages);
    }

    [Fact]
    public async Task TemplateCreateIsQueuedThenProcessedAsynchronously()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        var service = new TemplateManagementService(
            database.Wabas,
            database.Templates,
            database.Operations,
            database.Outbox,
            database.UnitOfWork,
            time);
        using var components = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Olá {{1}}\"}]");

        var queued = await service.QueueCreateAsync(
            new CreateTemplateCommand(
                "waba-123",
                "novo_template",
                "pt_BR",
                "UTILITY",
                components.RootElement.Clone()),
            CancellationToken.None);
        var outbox = await database.DbContext.Outbox
            .SingleAsync(item => item.EventType == IntegrationEventTypes.TemplateCreateRequested, CancellationToken.None);
        var requestedEvent = JsonSerializer.Deserialize<TemplateMutationRequestedEvent>(
            outbox.Payload,
            ApplicationJson.SerializerOptions)!;
        var processor = new TemplateMutationProcessor(
            database.Wabas,
            database.Templates,
            database.Operations,
            meta,
            database.UnitOfWork,
            time);
        var processed = await processor.ProcessAsync(requestedEvent, CancellationToken.None);

        Assert.Equal(queued.OperationId, processed.OperationId);
        Assert.Equal(OperationStatus.Succeeded, processed.Status);
        Assert.NotNull(processed.TemplateId);
        Assert.Single(meta.CreatedTemplates);
        var saved = await database.DbContext.Templates
            .SingleAsync(item => item.Name == "novo_template", CancellationToken.None);
        Assert.Equal("created-template", saved.MetaTemplateId);
        Assert.Equal("PENDING", saved.Status);
    }

    [Fact]
    public async Task TransientTemplateFailureRegistersOperationalRetry()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        meta.FailNextTemplate();
        var waba = await database.DbContext.Wabas.SingleAsync(CancellationToken.None);
        var operation = new IntegrationOperation(
            Guid.NewGuid(),
            waba.Id,
            OperationType.CreateTemplate,
            null,
            Now,
            Guid.NewGuid());
        database.Operations.Add(operation);
        await database.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        var requestedEvent = new TemplateMutationRequestedEvent(
            operation.Id,
            waba.Id,
            OperationType.CreateTemplate,
            null,
            "novo_template",
            "pt_BR",
            "UTILITY",
            "[{\"type\":\"BODY\",\"text\":\"Oi\"}]");
        var processor = new TemplateMutationProcessor(
            database.Wabas,
            database.Templates,
            database.Operations,
            meta,
            database.UnitOfWork,
            time);

        var exception = await Assert.ThrowsAsync<MessageProcessingException>(() =>
            processor.ProcessAsync(requestedEvent, CancellationToken.None));

        Assert.Equal(Now.AddSeconds(30), exception.RetryAt);
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(Now.AddSeconds(30), operation.NextAttemptAt);
    }

    private static SendNotificationHandler CreateSendHandler(TestDatabase database, TimeProvider timeProvider) => new(
        database.Wabas,
        database.PhoneNumbers,
        database.Definitions,
        database.Templates,
        database.Messages,
        database.Operations,
        database.Outbox,
        database.UnitOfWork,
        timeProvider);

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
}
