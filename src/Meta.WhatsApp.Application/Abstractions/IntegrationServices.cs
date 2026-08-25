namespace Meta.WhatsApp.Application.Abstractions;

public interface ISecretProvider
{
    Task<string> GetSecretAsync(string secretKey, CancellationToken cancellationToken);
}

public interface ICredentialProvider : ISecretProvider
{
    Task<string> GetAccessTokenAsync(string credentialKey, CancellationToken cancellationToken);
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        Guid correlationId,
        CancellationToken cancellationToken);
}

public interface IBlobStorage
{
    Task<StoredBlob> UploadAsync(
        string path,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);

    Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken);
}

public sealed record StoredBlob(string Path, Uri? Uri, long Size);

public interface IContentRenderer
{
    string Name { get; }

    string Version { get; }

    Task<RenderedContent> RenderAsync(string normalizedPayload, CancellationToken cancellationToken);
}

public sealed record RenderedContent(
    Stream Content,
    string FileName,
    string MimeType,
    int Width,
    int Height);

public interface IContentRendererResolver
{
    IContentRenderer Resolve(string name);
}
