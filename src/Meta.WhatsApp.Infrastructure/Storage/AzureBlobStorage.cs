using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Infrastructure.Storage;

public sealed class AzureBlobStorage : IBlobStorage
{
    private readonly BlobStorageOptions _options;
    private readonly TokenCredential _credential;
    private readonly object _containerLock = new();
    private BlobContainerClient? _container;

    public AzureBlobStorage(IOptions<BlobStorageOptions> options, TokenCredential credential)
    {
        _options = options.Value;
        _credential = credential;
    }

    public async Task<StoredBlob> UploadAsync(
        string path,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var container = GetContainer();
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var blob = container.GetBlobClient(Required(path, nameof(path)));
        await blob.UploadAsync(
                content,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = Required(contentType, nameof(contentType)) },
                    Metadata = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
                },
                cancellationToken)
            .ConfigureAwait(false);
        var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new StoredBlob(path, blob.Uri, properties.Value.ContentLength);
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        await GetContainer()
            .GetBlobClient(Required(path, nameof(path)))
            .OpenReadAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

    public async Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        await GetContainer()
            .GetBlobClient(Required(path, nameof(path)))
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private BlobContainerClient GetContainer()
    {
        if (_container is not null)
        {
            return _container;
        }

        lock (_containerLock)
        {
            var serviceUri = _options.ServiceUri ?? throw new InvalidOperationException(
                "Storage:ServiceUri must be configured before rendered media can be stored.");
            return _container ??= new BlobServiceClient(serviceUri, _credential)
                .GetBlobContainerClient(Required(_options.Container, nameof(_options.Container)));
        }
    }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A value is required.", parameterName)
            : value.Trim();
}
