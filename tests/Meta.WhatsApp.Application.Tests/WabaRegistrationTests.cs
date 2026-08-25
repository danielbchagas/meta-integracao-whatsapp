using System.Text.Json;
using Meta.WhatsApp.Application;
using Meta.WhatsApp.Application.Wabas;
using Meta.WhatsApp.Application.Tests.Support;
using Meta.WhatsApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace Meta.WhatsApp.Application.Tests;

public sealed class WabaRegistrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegistrationSynchronizesWabaPhonesTemplatesAndOutboxAtomically()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients
        {
            Templates = [FakeMetaClients.ApprovedTemplate("template-1", "proposta_aprovada")]
        };
        var handler = CreateHandler(database, meta, time);

        var result = await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod", Guid.Parse("11111111-1111-1111-1111-111111111111")),
            CancellationToken.None);

        Assert.Equal(WabaStatus.Active, result.Status);
        Assert.Equal(1, result.PhoneNumberCount);
        Assert.Equal(1, result.TemplateCount);
        var waba = await database.DbContext.Wabas.SingleAsync(CancellationToken.None);
        Assert.Equal("meta-prod", waba.CredentialKey);
        Assert.Single(database.DbContext.PhoneNumbers);
        Assert.Single(database.DbContext.Templates);
        var outbox = await database.DbContext.Outbox.SingleAsync(CancellationToken.None);
        Assert.Equal(IntegrationEventTypes.WabaSynchronized, outbox.EventType);
        Assert.DoesNotContain("access_token", outbox.Payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meta-prod", outbox.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconciliationIsIdempotentAndVersionsOnlyChangedComponents()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients
        {
            Templates = [FakeMetaClients.ApprovedTemplate("template-1", "proposta_aprovada")]
        };
        var handler = CreateHandler(database, meta, time);

        await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(5));
        await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None);

        Assert.Single(database.DbContext.Wabas);
        Assert.Single(database.DbContext.PhoneNumbers);
        Assert.Single(database.DbContext.Templates);
        Assert.Single(database.DbContext.TemplateVersions);

        using var changed = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Mudou {{1}}\"}]");
        meta.Templates =
        [
            FakeMetaClients.ApprovedTemplate("template-1", "proposta_aprovada") with
            {
                Components = changed.RootElement.Clone()
            }
        ];
        time.Advance(TimeSpan.FromMinutes(5));
        await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None);

        Assert.Equal(2, database.DbContext.TemplateVersions.Count());
        Assert.Equal(3, database.DbContext.Outbox.Count());
    }

    [Fact]
    public async Task RegistrationRejectsMismatchedMetaIdentifierWithoutPersistingState()
    {
        await using var database = new TestDatabase();
        var meta = new FakeMetaClients { Waba = new Meta.WhatsApp.Application.Abstractions.MetaWabaSnapshot("other", null, null) };
        var handler = CreateHandler(database, meta, new TestTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None));
        Assert.Empty(database.DbContext.Wabas);
        Assert.Empty(database.DbContext.Outbox);
    }

    [Fact]
    public async Task ReconciliationRemovesPhonesAndTemplatesNoLongerReturnedByMeta()
    {
        await using var database = new TestDatabase();
        var time = new TestTimeProvider(Now);
        var meta = new FakeMetaClients
        {
            Templates = [FakeMetaClients.ApprovedTemplate("template-1", "proposta_aprovada")]
        };
        var handler = CreateHandler(database, meta, time);
        await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None);
        meta.PhoneNumbers = [];
        meta.Templates = [];
        time.Advance(TimeSpan.FromMinutes(5));

        await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None);

        Assert.Empty(database.DbContext.PhoneNumbers);
        Assert.Empty(database.DbContext.Templates);
        Assert.Empty(database.DbContext.TemplateVersions);
    }

    private static RegisterWabaHandler CreateHandler(
        TestDatabase database,
        FakeMetaClients meta,
        TimeProvider timeProvider) => new(
        meta,
        meta,
        meta,
        database.Wabas,
        database.PhoneNumbers,
        database.Templates,
        database.Outbox,
        database.UnitOfWork,
        timeProvider);
}
