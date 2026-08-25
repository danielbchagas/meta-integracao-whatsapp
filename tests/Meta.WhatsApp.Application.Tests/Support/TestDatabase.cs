using Microsoft.EntityFrameworkCore;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Infrastructure.Persistence;

namespace Meta.WhatsApp.Application.Tests.Support;

internal sealed class TestDatabase : IAsyncDisposable
{
    public TestDatabase(string? name = null)
    {
        var options = new DbContextOptionsBuilder<WhatsAppDbContext>()
            .UseInMemoryDatabase(
                name ?? Guid.NewGuid().ToString("N"),
                options => options.EnableNullChecks(false))
            .EnableSensitiveDataLogging()
            .Options;
        DbContext = new WhatsAppDbContext(options);
        DbContext.Database.EnsureCreated();
        Repositories = new EfRepositories(DbContext);
    }

    public WhatsAppDbContext DbContext { get; }

    public EfRepositories Repositories { get; }

    public IWabaRepository Wabas => Repositories;

    public IPhoneNumberRepository PhoneNumbers => Repositories;

    public ITemplateRepository Templates => Repositories;

    public IMessageDefinitionRepository Definitions => Repositories;

    public IMessageRepository Messages => Repositories;

    public IRenderedMediaRepository Media => Repositories;

    public IOperationRepository Operations => Repositories;

    public IOutboxRepository Outbox => Repositories;

    public IInboxRepository Inbox => Repositories;

    public IWebhookEventStore WebhookStore => Repositories;

    public IUnitOfWork UnitOfWork => Repositories;

    public ValueTask DisposeAsync() => DbContext.DisposeAsync();
}
