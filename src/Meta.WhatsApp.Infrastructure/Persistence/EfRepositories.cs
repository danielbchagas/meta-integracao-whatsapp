using Microsoft.EntityFrameworkCore;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Infrastructure.Persistence;

public sealed class EfRepositories :
    IWabaRepository,
    IPhoneNumberRepository,
    ITemplateRepository,
    IMessageDefinitionRepository,
    IMessageRepository,
    IRenderedMediaRepository,
    IOperationRepository,
    IOutboxRepository,
    IInboxRepository,
    IWebhookEventStore,
    IUnitOfWork
{
    private readonly WhatsAppDbContext _dbContext;

    public EfRepositories(WhatsAppDbContext dbContext) => _dbContext = dbContext;

    async Task<IReadOnlyList<Waba>> IWabaRepository.GetAllAsync(CancellationToken cancellationToken) =>
        await _dbContext.Wabas
            .AsNoTracking()
            .OrderBy(item => item.MetaWabaId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    Task<Waba?> IWabaRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Wabas.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    Task<Waba?> IWabaRepository.GetByMetaIdAsync(string metaWabaId, CancellationToken cancellationToken) =>
        _dbContext.Wabas.SingleOrDefaultAsync(item => item.MetaWabaId == metaWabaId, cancellationToken);

    void IWabaRepository.Add(Waba waba) => _dbContext.Wabas.Add(waba);

    Task<PhoneNumber?> IPhoneNumberRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.PhoneNumbers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    Task<PhoneNumber?> IPhoneNumberRepository.GetByMetaIdAsync(
        string metaPhoneNumberId,
        CancellationToken cancellationToken) =>
        _dbContext.PhoneNumbers.SingleOrDefaultAsync(
            item => item.MetaPhoneNumberId == metaPhoneNumberId,
            cancellationToken);

    async Task<IReadOnlyList<PhoneNumber>> IPhoneNumberRepository.GetByWabaIdAsync(
        Guid wabaId,
        CancellationToken cancellationToken) =>
        await _dbContext.PhoneNumbers
            .Where(item => item.WabaId == wabaId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    void IPhoneNumberRepository.Add(PhoneNumber phoneNumber) => _dbContext.PhoneNumbers.Add(phoneNumber);

    void IPhoneNumberRepository.Remove(PhoneNumber phoneNumber) => _dbContext.PhoneNumbers.Remove(phoneNumber);

    Task<MessageTemplate?> ITemplateRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Templates
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    Task<MessageTemplate?> ITemplateRepository.GetByMetaIdAsync(
        string metaTemplateId,
        CancellationToken cancellationToken) =>
        _dbContext.Templates
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.MetaTemplateId == metaTemplateId, cancellationToken);

    Task<MessageTemplate?> ITemplateRepository.GetByNaturalKeyAsync(
        Guid wabaId,
        string name,
        string language,
        CancellationToken cancellationToken) =>
        _dbContext.Templates
            .Include(item => item.Versions)
            .SingleOrDefaultAsync(
                item => item.WabaId == wabaId && item.Name == name && item.Language == language,
                cancellationToken);

    async Task<IReadOnlyList<MessageTemplate>> ITemplateRepository.GetByWabaIdAsync(
        Guid wabaId,
        CancellationToken cancellationToken) =>
        await _dbContext.Templates
            .Include(item => item.Versions)
            .Where(item => item.WabaId == wabaId)
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Language)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    void ITemplateRepository.Add(MessageTemplate messageTemplate) => _dbContext.Templates.Add(messageTemplate);

    void ITemplateRepository.Remove(MessageTemplate messageTemplate) => _dbContext.Templates.Remove(messageTemplate);

    Task<MessageDefinition?> IMessageDefinitionRepository.GetAsync(
        string code,
        CancellationToken cancellationToken) =>
        _dbContext.MessageDefinitions.AsNoTracking().SingleOrDefaultAsync(item => item.Code == code, cancellationToken);

    async Task<IReadOnlyList<MessageDefinition>> IMessageDefinitionRepository.GetAllAsync(
        CancellationToken cancellationToken) =>
        await _dbContext.MessageDefinitions
            .AsNoTracking()
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    Task<WhatsAppMessage?> IMessageRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Messages.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    Task<WhatsAppMessage?> IMessageRepository.GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _dbContext.Messages.SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);

    Task<WhatsAppMessage?> IMessageRepository.GetByMetaMessageIdAsync(
        string metaMessageId,
        CancellationToken cancellationToken) =>
        _dbContext.Messages.SingleOrDefaultAsync(item => item.MetaMessageId == metaMessageId, cancellationToken);

    void IMessageRepository.Add(WhatsAppMessage message) => _dbContext.Messages.Add(message);

    Task<RenderedMedia?> IRenderedMediaRepository.GetByMessageIdAsync(
        Guid messageId,
        CancellationToken cancellationToken) =>
        _dbContext.RenderedMedia.SingleOrDefaultAsync(item => item.MessageId == messageId, cancellationToken);

    Task<RenderedMedia?> IRenderedMediaRepository.GetReusableAsync(
        string sha256,
        string renderer,
        string rendererVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _dbContext.RenderedMedia
            .AsNoTracking()
            .Where(item =>
                item.Sha256 == sha256 &&
                item.Renderer == renderer &&
                item.RendererVersion == rendererVersion &&
                (item.ExpiresAt == null || item.ExpiresAt > now))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    void IRenderedMediaRepository.Add(RenderedMedia media) => _dbContext.RenderedMedia.Add(media);

    Task<IntegrationOperation?> IOperationRepository.GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        _dbContext.Operations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    void IOperationRepository.Add(IntegrationOperation operation) => _dbContext.Operations.Add(operation);

    void IOutboxRepository.Add(OutboxMessage message) => _dbContext.Outbox.Add(message);

    async Task<IReadOnlyList<OutboxMessage>> IOutboxRepository.ClaimPendingAsync(
        int batchSize,
        string workerId,
        DateTimeOffset now,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var expiredClaim = now.Subtract(claimTimeout);
        if (!_dbContext.Database.IsSqlServer())
        {
            var localPending = await _dbContext.Outbox
                .Where(item =>
                    item.PublishedAt == null &&
                    (item.NextAttemptAt == null || item.NextAttemptAt <= now) &&
                    (item.ClaimedAt == null || item.ClaimedAt <= expiredClaim))
                .OrderBy(item => item.OccurredAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var message in localPending)
            {
                message.Claim(workerId, now);
            }

            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return localPending;
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var pending = await _dbContext.Outbox
            .FromSqlInterpolated($"""
                SELECT TOP ({batchSize}) *
                FROM integration_outbox WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE published_at IS NULL
                  AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                  AND (claimed_at IS NULL OR claimed_at <= {expiredClaim})
                ORDER BY occurred_at
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in pending)
        {
            message.Claim(workerId, now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return pending;
    }

    async Task IOutboxRepository.MarkPublishedAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var message = await _dbContext.Outbox.SingleAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        message.MarkPublished(now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task IOutboxRepository.RegisterFailureAsync(
        Guid id,
        string errorMessage,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken)
    {
        var message = await _dbContext.Outbox.SingleAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false);
        message.RegisterFailure(errorMessage, nextAttemptAt);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    Task<InboxMessage?> IInboxRepository.GetAsync(Guid messageId, CancellationToken cancellationToken) =>
        _dbContext.Inbox.SingleOrDefaultAsync(item => item.MessageId == messageId, cancellationToken);

    async Task<bool> IInboxRepository.TryAddAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        if (await _dbContext.Inbox.AnyAsync(item => item.MessageId == message.MessageId, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        _dbContext.Inbox.Add(message);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(message).State = EntityState.Detached;
            return false;
        }
    }

    async Task IInboxRepository.MarkProcessedAsync(
        Guid messageId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var message = await _dbContext.Inbox.SingleAsync(item => item.MessageId == messageId, cancellationToken)
            .ConfigureAwait(false);
        message.MarkProcessed(now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task IInboxRepository.RegisterFailureAsync(
        Guid messageId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var message = await _dbContext.Inbox.SingleAsync(item => item.MessageId == messageId, cancellationToken)
            .ConfigureAwait(false);
        message.RegisterFailure(errorMessage);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task<bool> IWebhookEventStore.TryEnqueueAsync(
        InboxMessage inboxMessage,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            if (await _dbContext.Inbox
                    .AnyAsync(item => item.MessageId == inboxMessage.MessageId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return false;
            }

            _dbContext.Inbox.Add(inboxMessage);
            _dbContext.Outbox.Add(outboxMessage);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (await _dbContext.Inbox
                .AnyAsync(item => item.MessageId == inboxMessage.MessageId, cancellationToken)
                .ConfigureAwait(false))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        _dbContext.Inbox.Add(inboxMessage);
        _dbContext.Outbox.Add(outboxMessage);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.Entry(inboxMessage).State = EntityState.Detached;
            _dbContext.Entry(outboxMessage).State = EntityState.Detached;
            return false;
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_dbContext.Database.IsRelational())
        {
            await action(cancellationToken).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var transaction = await _dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await action(cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
