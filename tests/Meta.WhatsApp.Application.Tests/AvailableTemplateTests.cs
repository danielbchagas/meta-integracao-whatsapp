using System.Text.Json;
using Meta.WhatsApp.Application.Queries;
using Meta.WhatsApp.Application.Tests.Support;
using Meta.WhatsApp.Application.Wabas;

namespace Meta.WhatsApp.Application.Tests;

public sealed class AvailableTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListsOnlyApprovedTemplatesAndMapsEnabledMessageTypes()
    {
        await using var database = new TestDatabase();
        var meta = new FakeMetaClients();
        using var pendingComponents = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Pendente\"}]");
        meta.Templates =
        [
            FakeMetaClients.ApprovedTemplate("template-1", "proposta_aprovada"),
            FakeMetaClients.ApprovedTemplate("template-2", "resumo_veiculos"),
            FakeMetaClients.ApprovedTemplate("template-3", "ainda_pendente") with
            {
                Status = "PENDING",
                Components = pendingComponents.RootElement.Clone()
            }
        ];
        await RegisterAsync(database, meta);
        var queries = CreateQueries(database);

        var result = await queries.GetAvailableTemplatesAsync("waba-123", CancellationToken.None);

        Assert.Collection(
            result,
            proposal =>
            {
                Assert.Equal("proposta_aprovada", proposal.Name);
                Assert.Equal(["PROPOSTA_APROVADA"], proposal.MessageTypes);
            },
            vehicles =>
            {
                Assert.Equal("resumo_veiculos", vehicles.Name);
                Assert.Empty(vehicles.MessageTypes);
            });
        Assert.DoesNotContain(result, item => item.Name == "ainda_pendente");
    }

    [Fact]
    public async Task MissingWabaIsReportedInsteadOfReturningAnAmbiguousEmptyList()
    {
        await using var database = new TestDatabase();
        var queries = CreateQueries(database);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            queries.GetAvailableTemplatesAsync("missing", CancellationToken.None));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    private static ManagementQueries CreateQueries(TestDatabase database) => new(
        database.Wabas,
        database.PhoneNumbers,
        database.Templates,
        database.Definitions,
        database.Operations,
        database.Messages);

    private static async Task RegisterAsync(TestDatabase database, FakeMetaClients meta)
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
            new TestTimeProvider(Now));
        await handler.HandleAsync(
            new RegisterWabaCommand("waba-123", "meta-prod"),
            CancellationToken.None);
    }
}
