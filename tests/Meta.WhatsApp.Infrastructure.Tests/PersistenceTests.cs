using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;
using Meta.WhatsApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Meta.WhatsApp.Infrastructure.Tests;

public sealed class PersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ModelUsesDomainAssignedIdsConcurrencyTokensAndRequiredUniqueIndexes()
    {
        using var context = CreateInMemoryContext();
        var model = context.Model;

        Assert.Equal(
            ValueGenerated.Never,
            model.FindEntityType(typeof(MessageTemplateVersion))!
                .FindProperty(nameof(MessageTemplateVersion.Id))!
                .ValueGenerated);
        Assert.True(model.FindEntityType(typeof(Waba))!
            .FindProperty(nameof(Waba.RowVersion))!
            .IsConcurrencyToken);
        Assert.Contains(
            model.FindEntityType(typeof(WhatsAppMessage))!.GetIndexes(),
            index => index.IsUnique && index.Properties.Single().Name == nameof(WhatsAppMessage.IdempotencyKey));
        Assert.Contains(
            model.FindEntityType(typeof(MessageTemplate))!.GetIndexes(),
            index => index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual([
                        nameof(MessageTemplate.WabaId),
                        nameof(MessageTemplate.Name),
                        nameof(MessageTemplate.Language)
                    ]));
    }

    [Fact]
    public void SqlServerModelGeneratesAllTablesAndOperationalIndexes()
    {
        var options = new DbContextOptionsBuilder<WhatsAppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=WhatsAppSchemaTests;Trusted_Connection=True")
            .Options;
        using var context = new WhatsAppDbContext(options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CREATE TABLE [whatsapp_waba]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [whatsapp_template_version]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [integration_outbox]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [integration_inbox]", script, StringComparison.Ordinal);
        Assert.Contains("idempotency_key", script, StringComparison.Ordinal);
        Assert.Contains("row_version", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutboxClaimHonorsScheduleLockExpiryAndPublication()
    {
        await using var context = CreateInMemoryContext();
        var repositories = new EfRepositories(context);
        var outbox = (IOutboxRepository)repositories;
        var available = new OutboxMessage(
            Guid.NewGuid(),
            "Message",
            "1",
            "event.available",
            "{}",
            Now.AddMinutes(-1),
            Guid.NewGuid());
        var delayed = new OutboxMessage(
            Guid.NewGuid(),
            "Message",
            "2",
            "event.delayed",
            "{}",
            Now,
            Guid.NewGuid());
        delayed.RegisterFailure("wait", Now.AddMinutes(10));
        outbox.Add(available);
        outbox.Add(delayed);
        await repositories.SaveChangesAsync(CancellationToken.None);

        var firstClaim = await outbox.ClaimPendingAsync(
            10,
            "worker-a",
            Now,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        var lockedClaim = await outbox.ClaimPendingAsync(
            10,
            "worker-b",
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        var expiredClaim = await outbox.ClaimPendingAsync(
            10,
            "worker-b",
            Now.AddMinutes(6),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        await outbox.MarkPublishedAsync(available.Id, Now.AddMinutes(7), CancellationToken.None);
        var afterPublication = await outbox.ClaimPendingAsync(
            10,
            "worker-c",
            Now.AddMinutes(7),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);

        Assert.Equal(available.Id, Assert.Single(firstClaim).Id);
        Assert.Empty(lockedClaim);
        Assert.Equal(available.Id, Assert.Single(expiredClaim).Id);
        Assert.Empty(afterPublication);
        Assert.Null(available.ClaimedBy);
        Assert.NotNull(available.PublishedAt);
    }

    [Fact]
    public async Task WebhookInboxAndOutboxAreInsertedAtomicallyAndDeduplicated()
    {
        await using var context = CreateInMemoryContext();
        var repositories = new EfRepositories(context);
        var store = (IWebhookEventStore)repositories;
        var messageId = Guid.NewGuid();

        var accepted = await store.TryEnqueueAsync(
            new InboxMessage(messageId, "webhook", "{}", Now),
            CreateOutbox(messageId),
            CancellationToken.None);
        var duplicate = await store.TryEnqueueAsync(
            new InboxMessage(messageId, "webhook", "{}", Now),
            CreateOutbox(messageId),
            CancellationToken.None);

        Assert.True(accepted);
        Assert.False(duplicate);
        Assert.Single(context.Inbox);
        Assert.Single(context.Outbox);
    }

    private static WhatsAppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<WhatsAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"), builder => builder.EnableNullChecks(false))
            .Options;
        var context = new WhatsAppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static OutboxMessage CreateOutbox(Guid inboxId) => new(
        Guid.NewGuid(),
        nameof(InboxMessage),
        inboxId.ToString("D"),
        "webhook.received",
        "{}",
        Now,
        Guid.NewGuid());
}
