using System.Net;
using System.Text.Json;
using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Application.Tests.Support;

internal sealed class FakeMetaClients :
    IWabaMetaClient,
    IPhoneNumberMetaClient,
    ITemplateMetaClient,
    IMediaMetaClient,
    IMessageMetaClient
{
    private readonly Queue<Exception> _sendFailures = new();
    private readonly Queue<Exception> _templateFailures = new();

    public MetaWabaSnapshot Waba { get; set; } = new("waba-123", "business-1", "WABA Teste");

    public IReadOnlyList<MetaPhoneNumberSnapshot> PhoneNumbers { get; set; } =
    [
        new("phone-1", "+55 11 99999-0000", "Empresa", "GREEN", "CLOUD_API", "CONNECTED")
    ];

    public IReadOnlyList<MetaTemplateSnapshot> Templates { get; set; } = [];

    public List<MetaSendMessageRequest> SentMessages { get; } = [];

    public List<MetaTemplateWriteRequest> CreatedTemplates { get; } = [];

    public List<MetaTemplateWriteRequest> UpdatedTemplates { get; } = [];

    public List<string> DeletedTemplates { get; } = [];

    public int UploadCount { get; private set; }

    public void FailNextSend(Exception? exception = null) =>
        _sendFailures.Enqueue(exception ?? TransientFailure());

    public void FailNextTemplate(Exception? exception = null) =>
        _templateFailures.Enqueue(exception ?? TransientFailure());

    public Task<MetaWabaSnapshot> GetAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Waba);
    }

    public Task<IReadOnlyList<MetaPhoneNumberSnapshot>> GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PhoneNumbers);
    }

    Task<IReadOnlyList<MetaTemplateSnapshot>> ITemplateMetaClient.GetAllAsync(
        MetaApiContext context,
        string wabaId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Templates);
    }

    public Task<MetaTemplateWriteResult> CreateAsync(
        MetaApiContext context,
        string wabaId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowTemplateFailureIfAny();
        CreatedTemplates.Add(request);
        return Task.FromResult(new MetaTemplateWriteResult("created-template", "PENDING", request.Category));
    }

    public Task UpdateAsync(
        MetaApiContext context,
        string templateId,
        MetaTemplateWriteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowTemplateFailureIfAny();
        UpdatedTemplates.Add(request);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        MetaApiContext context,
        string wabaId,
        string templateName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowTemplateFailureIfAny();
        DeletedTemplates.Add(templateName);
        return Task.CompletedTask;
    }

    public Task<MetaMediaUploadResult> UploadAsync(
        MetaApiContext context,
        string phoneNumberId,
        Stream content,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UploadCount++;
        return Task.FromResult(new MetaMediaUploadResult($"media-{UploadCount}"));
    }

    public Task<MetaSendMessageResult> SendAsync(
        MetaApiContext context,
        MetaSendMessageRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_sendFailures.TryDequeue(out var failure))
        {
            throw failure;
        }

        SentMessages.Add(request);
        return Task.FromResult(new MetaSendMessageResult($"wamid-{SentMessages.Count}", request.Recipient));
    }

    public static MetaApiException TransientFailure() => new(
        HttpStatusCode.TooManyRequests,
        "Rate limited",
        errorCode: 4,
        retryAfter: TimeSpan.FromSeconds(30));

    public static MetaTemplateSnapshot ApprovedTemplate(
        string id,
        string name,
        string language = "pt_BR")
    {
        using var document = JsonDocument.Parse("[{\"type\":\"BODY\",\"text\":\"Olá {{1}}\"}]");
        return new MetaTemplateSnapshot(id, name, language, "UTILITY", "APPROVED", document.RootElement.Clone());
    }

    private void ThrowTemplateFailureIfAny()
    {
        if (_templateFailures.TryDequeue(out var failure))
        {
            throw failure;
        }
    }
}
