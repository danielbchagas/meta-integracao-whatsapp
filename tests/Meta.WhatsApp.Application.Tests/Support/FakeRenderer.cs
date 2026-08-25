using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Application.Tests.Support;

internal sealed class FakeRenderer : IContentRenderer, IContentRendererResolver
{
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04
    ];

    public string Name => "proposal-summary";

    public string Version => "test-1";

    public int RenderCount { get; private set; }

    public Task<RenderedContent> RenderAsync(string normalizedPayload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RenderCount++;
        return Task.FromResult(new RenderedContent(
            new MemoryStream(PngBytes, writable: false),
            "summary.png",
            "image/png",
            1200,
            600));
    }

    public IContentRenderer Resolve(string name) =>
        string.Equals(name, Name, StringComparison.OrdinalIgnoreCase)
            ? this
            : throw new InvalidOperationException($"Unknown renderer '{name}'.");
}
