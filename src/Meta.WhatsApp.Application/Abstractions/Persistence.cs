using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Abstractions;

public interface IWabaRepository
{
    Task<IReadOnlyList<Waba>> GetAllAsync(CancellationToken cancellationToken);

    Task<Waba?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Waba?> GetByMetaIdAsync(string metaWabaId, CancellationToken cancellationToken);

    void Add(Waba waba);
}

public interface IPhoneNumberRepository
{
    Task<PhoneNumber?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PhoneNumber?> GetByMetaIdAsync(string metaPhoneNumberId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PhoneNumber>> GetByWabaIdAsync(Guid wabaId, CancellationToken cancellationToken);

    void Add(PhoneNumber phoneNumber);

    void Remove(PhoneNumber phoneNumber);
}

public interface ITemplateRepository
{
    Task<MessageTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<MessageTemplate?> GetByMetaIdAsync(string metaTemplateId, CancellationToken cancellationToken);

    Task<MessageTemplate?> GetByNaturalKeyAsync(
        Guid wabaId,
        string name,
        string language,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageTemplate>> GetByWabaIdAsync(Guid wabaId, CancellationToken cancellationToken);

    void Add(MessageTemplate messageTemplate);

    void Remove(MessageTemplate messageTemplate);
}

public interface IMessageDefinitionRepository
{
    Task<MessageDefinition?> GetAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<MessageDefinition>> GetAllAsync(CancellationToken cancellationToken);
}

public interface IMessageRepository
{
    Task<WhatsAppMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<WhatsAppMessage?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<WhatsAppMessage?> GetByMetaMessageIdAsync(string metaMessageId, CancellationToken cancellationToken);

    void Add(WhatsAppMessage message);
}

public interface IRenderedMediaRepository
{
    Task<RenderedMedia?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken);

    Task<RenderedMedia?> GetReusableAsync(
        string sha256,
        string renderer,
        string rendererVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    void Add(RenderedMedia media);
}

public interface IOperationRepository
{
    Task<IntegrationOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(IntegrationOperation operation);
}

public interface IOutboxRepository
{
    void Add(OutboxMessage message);

    Task<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        int batchSize,
        string workerId,
        DateTimeOffset now,
        TimeSpan claimTimeout,
        CancellationToken cancellationToken);

    Task MarkPublishedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken);

    Task RegisterFailureAsync(
        Guid id,
        string errorMessage,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken);
}

public interface IInboxRepository
{
    Task<InboxMessage?> GetAsync(Guid messageId, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(InboxMessage message, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid messageId, DateTimeOffset now, CancellationToken cancellationToken);

    Task RegisterFailureAsync(Guid messageId, string errorMessage, CancellationToken cancellationToken);
}

public interface IWebhookEventStore
{
    Task<bool> TryEnqueueAsync(
        InboxMessage inboxMessage,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);
}
