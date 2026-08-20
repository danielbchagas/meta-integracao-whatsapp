using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meta.WhatsApp.Exceptions;
using Meta.WhatsApp.Internal;
using Meta.WhatsApp.Messages;
using Meta.WhatsApp.Sessions;
using Meta.WhatsApp.Templates;

namespace Meta.WhatsApp;

/// <summary>
/// Reusable, thread-safe client for the WhatsApp Cloud API and its template-management endpoints.
/// </summary>
public sealed class MetaWhatsAppClient : IMetaWhatsAppClient
{
    private const string TemplateFields = "id,name,language,category,status,components";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MetaWhatsAppOptions _options;
    private readonly IConversationSessionStore _sessionStore;
    private readonly TimeProvider _timeProvider;
    private readonly Uri _versionedBaseAddress;

    public MetaWhatsAppClient(
        HttpClient httpClient,
        MetaWhatsAppOptions options,
        IConversationSessionStore? sessionStore = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _httpClient = httpClient;
        _options = options;
        _sessionStore = sessionStore ?? new InMemoryConversationSessionStore();
        _timeProvider = timeProvider ?? TimeProvider.System;

        var baseAddress = options.GraphApiBaseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? options.GraphApiBaseAddress
            : new Uri(options.GraphApiBaseAddress.AbsoluteUri + '/', UriKind.Absolute);
        _versionedBaseAddress = new Uri(baseAddress, options.GraphApiVersion + '/');
    }

    public Task<SendMessageResult> SendTextMessageAsync(
        string recipient,
        string body,
        bool previewUrl = false,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(
            new OutboundMessage(recipient, new TextMessageContent(body, previewUrl)),
            cancellationToken);

    public Task<SendMessageResult> SendTemplateMessageAsync(
        string recipient,
        string templateName,
        string languageCode,
        IReadOnlyList<TemplateMessageComponent>? components = null,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(
            new OutboundMessage(
                recipient,
                new TemplateMessageContent(templateName, languageCode, components)),
            cancellationToken);

    public async Task<SendMessageResult> SendMessageAsync(
        OutboundMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Content);

        var recipient = NormalizeRecipient(message.Recipient);
        message.Content.Validate();

        var session = await GetOpenSessionAsync(recipient, cancellationToken).ConfigureAwait(false);
        if (message.Content.RequiresOpenSession &&
            _options.RequireOpenSessionForFreeFormMessages &&
            session is null)
        {
            throw new ConversationSessionClosedException(recipient);
        }

        var payload = message.Content is CustomMessageContent custom
            ? (object)custom.Payload
            : message.Content;

        var requestBody = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = recipient,
            ["type"] = message.Content.Type,
            [message.Content.Type] = payload
        };

        var replyToMessageId = !string.IsNullOrWhiteSpace(message.ReplyToMessageId)
            ? message.ReplyToMessageId
            : _options.AttachReplyContextToOpenSession && message.UseOpenSessionContext
                ? session?.LastInboundMessageId
                : null;

        if (!string.IsNullOrWhiteSpace(replyToMessageId))
        {
            requestBody["context"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["message_id"] = replyToMessageId
            };
        }

        var response = await SendAsync<SendMessageResponse>(
                HttpMethod.Post,
                $"{EscapePath(_options.PhoneNumberId)}/messages",
                requestBody,
                cancellationToken)
            .ConfigureAwait(false);

        var sentMessage = response.Messages.FirstOrDefault()
            ?? throw new MetaWhatsAppApiException(
                HttpStatusCode.OK,
                "Meta returned a successful response without a message identifier.");
        var contact = response.Contacts?.FirstOrDefault();

        return new SendMessageResult(sentMessage.Id, contact?.WhatsAppId, contact?.Input);
    }

    public async Task<ConversationSession> RegisterInboundMessageAsync(
        InboundMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var recipient = NormalizeRecipient(message.Recipient);
        if (string.IsNullOrWhiteSpace(message.MessageId))
        {
            throw new ArgumentException("Inbound message ID is required.", nameof(message));
        }

        var receivedAtUtc = message.ReceivedAtUtc ?? _timeProvider.GetUtcNow();
        var session = new ConversationSession(
            _options.PhoneNumberId,
            recipient,
            message.MessageId,
            receivedAtUtc,
            receivedAtUtc.Add(_options.CustomerServiceWindow),
            ConversationSessionState.Open,
            ReengagementAttempts: []);

        return await _sessionStore.RegisterInboundAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConversationSession?> GetOpenSessionAsync(
        string recipient,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(recipient, cancellationToken).ConfigureAwait(false);
        return session?.IsOpen(_timeProvider.GetUtcNow()) == true ? session : null;
    }

    public async Task<ConversationSession?> GetSessionAsync(
        string recipient,
        CancellationToken cancellationToken = default)
    {
        var normalizedRecipient = NormalizeRecipient(recipient);
        var nowUtc = _timeProvider.GetUtcNow();
        var session = await _sessionStore
            .GetAsync(_options.PhoneNumberId, normalizedRecipient, cancellationToken)
            .ConfigureAwait(false);
        if (session is null || session.State != ConversationSessionState.Open || session.ExpiresAtUtc > nowUtc)
        {
            return session;
        }

        return await _sessionStore
            .MarkExpiredAsync(
                _options.PhoneNumberId,
                normalizedRecipient,
                nowUtc,
                force: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CloseSessionAsync(string recipient, CancellationToken cancellationToken = default)
    {
        await _sessionStore
            .MarkExpiredAsync(
                _options.PhoneNumberId,
                NormalizeRecipient(recipient),
                _timeProvider.GetUtcNow(),
                force: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReengagementResult> ReengageAsync(
        ReengagementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recipient = NormalizeRecipient(request.Recipient);
        RequireValue(request.TemplateName, nameof(request.TemplateName));
        RequireValue(request.LanguageCode, nameof(request.LanguageCode));
        RequireValue(request.IdempotencyKey, nameof(request.IdempotencyKey));

        var session = await GetSessionAsync(recipient, cancellationToken).ConfigureAwait(false)
            ?? throw new ConversationSessionNotFoundException(_options.PhoneNumberId, recipient);
        var priorAttempt = session.ReengagementAttempts.FirstOrDefault(attempt =>
            string.Equals(attempt.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
        if (priorAttempt is not null)
        {
            return ToReengagementResult(recipient, priorAttempt);
        }

        if (session.IsOpen(_timeProvider.GetUtcNow()))
        {
            throw new ConversationSessionStillOpenException(
                _options.PhoneNumberId,
                recipient,
                session.ExpiresAtUtc);
        }

        var templates = await GetTemplatesAsync(request.TemplateName, cancellationToken).ConfigureAwait(false);
        var template = templates.FirstOrDefault(item =>
            string.Equals(item.Name, request.TemplateName, StringComparison.Ordinal) &&
            string.Equals(item.Language, request.LanguageCode, StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(template?.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReengagementTemplateNotApprovedException(
                request.TemplateName,
                request.LanguageCode,
                template?.Status);
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var attempt = new ReengagementAttempt(
            request.IdempotencyKey,
            request.TemplateName,
            request.LanguageCode,
            nowUtc,
            ReengagementMessageStatus.Submitting,
            nowUtc);
        var reservation = await _sessionStore.TryReserveReengagementAsync(
                _options.PhoneNumberId,
                recipient,
                attempt,
                _options.ReengagementCooldown,
                _options.MaxReengagementHistory,
                cancellationToken)
            .ConfigureAwait(false);

        switch (reservation.Outcome)
        {
            case ReengagementReservationOutcome.Duplicate:
                return ToReengagementResult(
                    recipient,
                    reservation.Attempt ?? throw new InvalidOperationException("Duplicate reservation has no attempt."));
            case ReengagementReservationOutcome.CooldownActive:
                throw new ReengagementCooldownException(
                    recipient,
                    reservation.RetryAtUtc ?? nowUtc.Add(_options.ReengagementCooldown));
            case ReengagementReservationOutcome.SessionNotFound:
                throw new ConversationSessionNotFoundException(_options.PhoneNumberId, recipient);
            case ReengagementReservationOutcome.SessionOpen:
                throw new ConversationSessionStillOpenException(
                    _options.PhoneNumberId,
                    recipient,
                    reservation.Session?.ExpiresAtUtc ?? nowUtc);
            case ReengagementReservationOutcome.Reserved:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reservation.Outcome));
        }

        try
        {
            var sent = await SendMessageAsync(
                    new OutboundMessage(
                        recipient,
                        new TemplateMessageContent(
                            request.TemplateName,
                            request.LanguageCode,
                            request.Components),
                        UseOpenSessionContext: false),
                    cancellationToken)
                .ConfigureAwait(false);
            await _sessionStore.UpdateReengagementAsync(
                    _options.PhoneNumberId,
                    recipient,
                    request.IdempotencyKey,
                    ReengagementMessageStatus.Accepted,
                    _timeProvider.GetUtcNow(),
                    sent.MessageId,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            return new ReengagementResult(
                ReengagementAction.Submitted,
                _options.PhoneNumberId,
                recipient,
                request.IdempotencyKey,
                ReengagementMessageStatus.Accepted,
                sent.MessageId);
        }
        catch (MetaWhatsAppApiException exception) when ((int)exception.StatusCode >= 400)
        {
            await _sessionStore.UpdateReengagementAsync(
                    _options.PhoneNumberId,
                    recipient,
                    request.IdempotencyKey,
                    ReengagementMessageStatus.Failed,
                    _timeProvider.GetUtcNow(),
                    errorCode: exception.ErrorCode?.ToString(),
                    errorMessage: exception.Message,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            await _sessionStore.UpdateReengagementAsync(
                    _options.PhoneNumberId,
                    recipient,
                    request.IdempotencyKey,
                    ReengagementMessageStatus.Unknown,
                    _timeProvider.GetUtcNow(),
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ConversationSession?> RegisterReengagementStatusAsync(
        ReengagementStatusUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var recipient = NormalizeRecipient(update.Recipient);
        RequireValue(update.MessageId, nameof(update.MessageId));
        if (update.Status is not (
                ReengagementMessageStatus.Sent or
                ReengagementMessageStatus.Delivered or
                ReengagementMessageStatus.Read or
                ReengagementMessageStatus.Failed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(update),
                update.Status,
                "Webhook status must be Sent, Delivered, Read, or Failed.");
        }

        return await _sessionStore.UpdateReengagementStatusAsync(
                _options.PhoneNumberId,
                recipient,
                update.MessageId,
                update.Status,
                update.OccurredAtUtc ?? _timeProvider.GetUtcNow(),
                update.ErrorCode,
                update.ErrorMessage,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WhatsAppTemplate>> GetTemplatesAsync(
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        var templates = new List<WhatsAppTemplate>();
        string? after = null;
        var visitedCursors = new HashSet<string>(StringComparer.Ordinal);

        do
        {
            var query = new List<string>
            {
                $"fields={Uri.EscapeDataString(TemplateFields)}",
                "limit=100"
            };
            if (!string.IsNullOrWhiteSpace(name))
            {
                query.Add($"name={Uri.EscapeDataString(name)}");
            }

            if (!string.IsNullOrWhiteSpace(after))
            {
                query.Add($"after={Uri.EscapeDataString(after)}");
            }

            var response = await SendAsync<TemplateListResponse>(
                    HttpMethod.Get,
                    $"{EscapePath(_options.BusinessAccountId)}/message_templates?{string.Join('&', query)}",
                    body: null,
                    cancellationToken)
                .ConfigureAwait(false);
            templates.AddRange(response.Data);

            var nextCursor = string.IsNullOrWhiteSpace(response.Paging?.Next)
                ? null
                : response.Paging?.Cursors?.After;
            after = nextCursor is not null && visitedCursors.Add(nextCursor)
                ? nextCursor
                : null;
        }
        while (after is not null);

        return templates;
    }

    public async Task<WhatsAppTemplate?> GetTemplateByIdAsync(
        string templateId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(templateId, nameof(templateId));
        try
        {
            return await SendAsync<WhatsAppTemplate>(
                    HttpMethod.Get,
                    $"{EscapePath(templateId)}?fields={Uri.EscapeDataString(TemplateFields)}",
                    body: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MetaWhatsAppApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CreateTemplateResult> CreateTemplateAsync(
        TemplateDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        var response = await SendAsync<CreateTemplateResponse>(
                HttpMethod.Post,
                $"{EscapePath(_options.BusinessAccountId)}/message_templates",
                ToTemplateWriteRequest(definition),
                cancellationToken)
            .ConfigureAwait(false);

        return new CreateTemplateResult(response.Id, response.Status, response.Category);
    }

    public async Task UpdateTemplateAsync(
        string templateId,
        TemplateDefinition definition,
        CancellationToken cancellationToken = default)
    {
        RequireValue(templateId, nameof(templateId));
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        var response = await SendAsync<SuccessResponse>(
                HttpMethod.Post,
                EscapePath(templateId),
                ToTemplateWriteRequest(definition),
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            throw new MetaWhatsAppApiException(HttpStatusCode.OK, "Meta did not confirm the template update.");
        }
    }

    public async Task<TemplateSynchronizationResult> EnsureTemplateAsync(
        TemplateDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();

        var existingTemplates = await GetTemplatesAsync(definition.Name, cancellationToken).ConfigureAwait(false);
        var existing = existingTemplates.FirstOrDefault(template =>
            string.Equals(template.Name, definition.Name, StringComparison.Ordinal) &&
            string.Equals(template.Language, definition.Language, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            var created = await CreateTemplateAsync(definition, cancellationToken).ConfigureAwait(false);
            return new TemplateSynchronizationResult(
                TemplateSynchronizationAction.Created,
                created.Id,
                created.Status,
                created.Category);
        }

        if (TemplateMatches(existing, definition))
        {
            return new TemplateSynchronizationResult(
                TemplateSynchronizationAction.Unchanged,
                existing.Id,
                existing.Status,
                existing.Category);
        }

        await UpdateTemplateAsync(existing.Id, definition, cancellationToken).ConfigureAwait(false);
        return new TemplateSynchronizationResult(
            TemplateSynchronizationAction.Updated,
            existing.Id,
            Status: null,
            definition.Category.ToUpperInvariant());
    }

    private static object ToTemplateWriteRequest(TemplateDefinition definition) => new
    {
        name = definition.Name,
        language = definition.Language,
        category = definition.Category.ToUpperInvariant(),
        components = definition.Components
    };

    private ReengagementResult ToReengagementResult(
        string recipient,
        ReengagementAttempt attempt)
    {
        var action = attempt.Status == ReengagementMessageStatus.Failed
            ? ReengagementAction.PreviouslyFailed
            : string.IsNullOrWhiteSpace(attempt.MessageId)
                ? ReengagementAction.InProgress
                : ReengagementAction.AlreadySubmitted;
        return new ReengagementResult(
            action,
            _options.PhoneNumberId,
            recipient,
            attempt.IdempotencyKey,
            attempt.Status,
            attempt.MessageId);
    }

    private static bool TemplateMatches(WhatsAppTemplate existing, TemplateDefinition desired)
    {
        if (!string.Equals(existing.Category, desired.Category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existingNode = JsonSerializer.SerializeToNode(existing.Components, SerializerOptions);
        var desiredNode = JsonSerializer.SerializeToNode(desired.Components, SerializerOptions);
        return JsonNode.DeepEquals(existingNode, desiredNode);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativePathAndQuery,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_versionedBaseAddress, relativePathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, response.ReasonPhrase, responseBody);
        }

        try
        {
            return JsonSerializer.Deserialize<T>(responseBody, SerializerOptions)
                ?? throw new JsonException("The response body was empty.");
        }
        catch (JsonException exception)
        {
            throw new MetaWhatsAppApiException(
                response.StatusCode,
                "Meta returned an invalid JSON response.",
                responseBody: responseBody,
                innerException: exception);
        }
    }

    private static MetaWhatsAppApiException CreateApiException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string responseBody)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<GraphErrorEnvelope>(responseBody, SerializerOptions);
            var error = envelope?.Error;
            return new MetaWhatsAppApiException(
                statusCode,
                error?.Message ?? $"Meta Graph API returned {(int)statusCode} {reasonPhrase}.",
                error?.Code,
                error?.ErrorSubcode,
                error?.Type,
                error?.TraceId,
                responseBody);
        }
        catch (JsonException exception)
        {
            return new MetaWhatsAppApiException(
                statusCode,
                $"Meta Graph API returned {(int)statusCode} {reasonPhrase}.",
                responseBody: responseBody,
                innerException: exception);
        }
    }

    private static string NormalizeRecipient(string recipient)
    {
        RequireValue(recipient, nameof(recipient));

        var builder = new StringBuilder(recipient.Length);
        foreach (var character in recipient)
        {
            if (char.IsAsciiDigit(character))
            {
                builder.Append(character);
                continue;
            }

            if (character is '+' or '-' or '(' or ')' or '.' || char.IsWhiteSpace(character))
            {
                continue;
            }

            throw new ArgumentException(
                "Recipient must be a phone number with country code.",
                nameof(recipient));
        }

        if (builder.Length == 0)
        {
            throw new ArgumentException("Recipient must contain digits.", nameof(recipient));
        }

        return builder.ToString();
    }

    private static string EscapePath(string value) => Uri.EscapeDataString(value);

    private static void RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }
    }
}
