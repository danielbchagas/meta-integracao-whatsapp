using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Wabas;

public sealed record RegisterWabaCommand(string WabaId, string CredentialKey, Guid? CorrelationId = null);

public sealed record RegisterWabaResult(
    Guid Id,
    string MetaWabaId,
    WabaStatus Status,
    int PhoneNumberCount,
    int TemplateCount,
    DateTimeOffset SynchronizedAt);

public sealed class RegisterWabaHandler
{
    private readonly IWabaMetaClient _wabaClient;
    private readonly IPhoneNumberMetaClient _phoneNumberClient;
    private readonly ITemplateMetaClient _templateClient;
    private readonly IWabaRepository _wabas;
    private readonly IPhoneNumberRepository _phoneNumbers;
    private readonly ITemplateRepository _templates;
    private readonly IOutboxRepository _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RegisterWabaHandler(
        IWabaMetaClient wabaClient,
        IPhoneNumberMetaClient phoneNumberClient,
        ITemplateMetaClient templateClient,
        IWabaRepository wabas,
        IPhoneNumberRepository phoneNumbers,
        ITemplateRepository templates,
        IOutboxRepository outbox,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _wabaClient = wabaClient;
        _phoneNumberClient = phoneNumberClient;
        _templateClient = templateClient;
        _wabas = wabas;
        _phoneNumbers = phoneNumbers;
        _templates = templates;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<RegisterWabaResult> HandleAsync(
        RegisterWabaCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var metaWabaId = Required(command.WabaId, nameof(command.WabaId));
        var credentialKey = Required(command.CredentialKey, nameof(command.CredentialKey));
        var context = new MetaApiContext(credentialKey);

        var wabaSnapshot = await _wabaClient
            .GetAsync(context, metaWabaId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(wabaSnapshot.Id, metaWabaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Meta returned a different WABA identifier.");
        }

        var phonesTask = _phoneNumberClient.GetAllAsync(context, metaWabaId, cancellationToken);
        var templatesTask = _templateClient.GetAllAsync(context, metaWabaId, cancellationToken);
        await Task.WhenAll(phonesTask, templatesTask).ConfigureAwait(false);

        var phoneSnapshots = await phonesTask.ConfigureAwait(false);
        var templateSnapshots = await templatesTask.ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var correlationId = command.CorrelationId ?? Guid.NewGuid();
        Waba? synchronizedWaba = null;

        await _unitOfWork.ExecuteInTransactionAsync(
            async transactionCancellationToken =>
            {
                synchronizedWaba = await _wabas
                    .GetByMetaIdAsync(metaWabaId, transactionCancellationToken)
                    .ConfigureAwait(false);
                if (synchronizedWaba is null)
                {
                    synchronizedWaba = Waba.Register(metaWabaId, credentialKey, now);
                    _wabas.Add(synchronizedWaba);
                }

                synchronizedWaba.Synchronize(
                    wabaSnapshot.BusinessId,
                    wabaSnapshot.Name,
                    credentialKey,
                    now);

                await SynchronizePhoneNumbersAsync(
                        synchronizedWaba.Id,
                        phoneSnapshots,
                        now,
                        transactionCancellationToken)
                    .ConfigureAwait(false);
                await SynchronizeTemplatesAsync(
                        synchronizedWaba.Id,
                        templateSnapshots,
                        now,
                        transactionCancellationToken)
                    .ConfigureAwait(false);

                var integrationEvent = new WabaSynchronizedEvent(
                    synchronizedWaba.Id,
                    synchronizedWaba.MetaWabaId,
                    phoneSnapshots.Count,
                    templateSnapshots.Count,
                    now);
                _outbox.Add(new OutboxMessage(
                    Guid.NewGuid(),
                    nameof(Waba),
                    synchronizedWaba.Id.ToString("D"),
                    IntegrationEventTypes.WabaSynchronized,
                    ApplicationJson.Serialize(integrationEvent),
                    now,
                    correlationId));
            },
            cancellationToken).ConfigureAwait(false);

        return new RegisterWabaResult(
            synchronizedWaba!.Id,
            synchronizedWaba.MetaWabaId,
            synchronizedWaba.Status,
            phoneSnapshots.Count,
            templateSnapshots.Count,
            now);
    }

    private async Task SynchronizePhoneNumbersAsync(
        Guid wabaId,
        IReadOnlyList<MetaPhoneNumberSnapshot> snapshots,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _phoneNumbers.GetByWabaIdAsync(wabaId, cancellationToken).ConfigureAwait(false);
        var snapshotsById = snapshots.ToDictionary(snapshot => snapshot.Id, StringComparer.Ordinal);

        foreach (var phone in existing)
        {
            if (!snapshotsById.Remove(phone.MetaPhoneNumberId, out var snapshot))
            {
                _phoneNumbers.Remove(phone);
                continue;
            }

            phone.ApplySnapshot(
                snapshot.DisplayPhoneNumber,
                snapshot.VerifiedName,
                snapshot.QualityRating,
                snapshot.PlatformType,
                snapshot.Status,
                now);
        }

        foreach (var snapshot in snapshotsById.Values)
        {
            _phoneNumbers.Add(new PhoneNumber(
                Guid.NewGuid(),
                wabaId,
                snapshot.Id,
                snapshot.DisplayPhoneNumber,
                snapshot.VerifiedName,
                snapshot.QualityRating,
                snapshot.PlatformType,
                snapshot.Status,
                now));
        }
    }

    private async Task SynchronizeTemplatesAsync(
        Guid wabaId,
        IReadOnlyList<MetaTemplateSnapshot> snapshots,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _templates.GetByWabaIdAsync(wabaId, cancellationToken).ConfigureAwait(false);
        var templatesByNaturalKey = existing.ToDictionary(
            template => NaturalKey(template.Name, template.Language),
            StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            var componentsJson = snapshot.Components.GetRawText();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(componentsJson)));
            if (!templatesByNaturalKey.Remove(
                    NaturalKey(snapshot.Name, snapshot.Language),
                    out var template))
            {
                _templates.Add(new MessageTemplate(
                    Guid.NewGuid(),
                    wabaId,
                    snapshot.Id,
                    snapshot.Name,
                    snapshot.Language,
                    snapshot.Category,
                    snapshot.Status,
                    componentsJson,
                    hash,
                    now));
                continue;
            }

            template.ApplySnapshot(
                snapshot.Id,
                snapshot.Category,
                snapshot.Status,
                componentsJson,
                hash,
                now);
        }

        foreach (var removedTemplate in templatesByNaturalKey.Values)
        {
            _templates.Remove(removedTemplate);
        }
    }

    private static string NaturalKey(string name, string language) => $"{name.Trim()}\n{language.Trim()}";

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
}
