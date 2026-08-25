using System.Net;
using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Application.Templates;
using Meta.WhatsApp.Application.Tests.Support;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Meta.WhatsApp.Application.Tests;

public sealed class TemplateLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DuplicateNaturalKeyIsRejectedBeforeAnOperationIsQueued()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients
        {
            Templates = [FakeMetaClients.ApprovedTemplate("template-1", "proposta_aprovada")]
        };
        await RegisterAsync(database, meta, time);
        var service = CreateService(database, time);
        using var components = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Duplicado\"}]");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueueCreateAsync(
            new CreateTemplateCommand(
                "waba-123",
                "proposta_aprovada",
                "pt_BR",
                "UTILITY",
                components.RootElement.Clone()),
            CancellationToken.None));

        Assert.Empty(database.DbContext.Operations);
        Assert.DoesNotContain(database.DbContext.Outbox, item =>
            item.EventType == IntegrationEventTypes.TemplateCreateRequested);
    }

    [Fact]
    public async Task UpdateCreatesVersionAndDeleteRemovesTemplateAfterMetaConfirms()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients
        {
            Templates = [FakeMetaClients.ApprovedTemplate("template-1", "proposta_aprovada")]
        };
        await RegisterAsync(database, meta, time);
        var service = CreateService(database, time);
        var processor = CreateProcessor(database, meta, time);
        var template = await database.DbContext.Templates.SingleAsync(CancellationToken.None);
        using var updatedComponents = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Atualizado {{1}}\"}]");

        var update = await service.QueueUpdateAsync(
            new UpdateTemplateCommand(
                "waba-123",
                template.Id,
                "MARKETING",
                updatedComponents.RootElement.Clone()),
            CancellationToken.None);
        var updateEvent = await GetEventAsync(database, update.OperationId);
        var updateResult = await processor.ProcessAsync(updateEvent, CancellationToken.None);
        var delete = await service.QueueDeleteAsync(
            new DeleteTemplateCommand("waba-123", template.Id),
            CancellationToken.None);
        var deleteEvent = await GetEventAsync(database, delete.OperationId);
        var deleteResult = await processor.ProcessAsync(deleteEvent, CancellationToken.None);

        Assert.Equal(OperationStatus.Succeeded, updateResult.Status);
        Assert.Equal(OperationStatus.Succeeded, deleteResult.Status);
        Assert.Single(meta.UpdatedTemplates);
        Assert.Equal("MARKETING", meta.UpdatedTemplates[0].Category);
        Assert.Equal(["proposta_aprovada"], meta.DeletedTemplates);
        Assert.Empty(database.DbContext.Templates);
        Assert.Empty(database.DbContext.TemplateVersions);
    }

    [Fact]
    public async Task NonRetryableMetaFailureCompletesOperationAsPermanentFailure()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients();
        await RegisterAsync(database, meta, time);
        meta.FailNextTemplate(new MetaApiException(HttpStatusCode.BadRequest, "Invalid template", errorCode: 100));
        var service = CreateService(database, time);
        var processor = CreateProcessor(database, meta, time);
        using var components = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Inválido\"}]");
        var queued = await service.QueueCreateAsync(
            new CreateTemplateCommand(
                "waba-123",
                "template_invalido",
                "pt_BR",
                "UTILITY",
                components.RootElement.Clone()),
            CancellationToken.None);
        var requested = await GetEventAsync(database, queued.OperationId);

        var result = await processor.ProcessAsync(requested, CancellationToken.None);

        Assert.Equal(OperationStatus.FailedPermanent, result.Status);
        var operation = await database.DbContext.Operations.SingleAsync(
            item => item.Id == queued.OperationId,
            CancellationToken.None);
        Assert.Equal(1, operation.Attempts);
        Assert.Null(operation.NextAttemptAt);
        Assert.Empty(database.DbContext.Templates);
    }

    private static TemplateManagementService CreateService(TestDatabase database, TimeProvider timeProvider) => new(
        database.Wabas,
        database.Templates,
        database.Operations,
        database.Outbox,
        database.UnitOfWork,
        timeProvider);

    private static TemplateMutationProcessor CreateProcessor(
        TestDatabase database,
        FakeMetaClients meta,
        TimeProvider timeProvider) => new(
        database.Wabas,
        database.Templates,
        database.Operations,
        meta,
        database.UnitOfWork,
        timeProvider);

    private static async Task<TemplateMutationRequestedEvent> GetEventAsync(
        TestDatabase database,
        Guid operationId)
    {
        var outbox = await database.DbContext.Outbox.SingleAsync(
            item => item.Payload.Contains(operationId.ToString("D")),
            CancellationToken.None);
        return JsonSerializer.Deserialize<TemplateMutationRequestedEvent>(
            outbox.Payload,
            ApplicationJson.SerializerOptions)!;
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
}
