using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Application.Tests.Support;

internal sealed class InMemoryBlobStorage : IBlobStorage
{
    private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public int UploadCount { get; private set; }

    public Task<StoredBlob> UploadAsync(
        string path,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();
        _blobs[path] = bytes;
        UploadCount++;
        return Task.FromResult(new StoredBlob(path, new Uri($"https://storage.test/{path}"), bytes.LongLength));
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new MemoryStream(_blobs[path], writable: false));
    }

    public Task DeleteIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _blobs.Remove(path);
        return Task.CompletedTask;
    }
}
