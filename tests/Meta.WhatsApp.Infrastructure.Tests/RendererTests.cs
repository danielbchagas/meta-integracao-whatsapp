using Meta.WhatsApp.Application.Abstractions;
using Meta.WhatsApp.Infrastructure.Rendering;

namespace Meta.WhatsApp.Infrastructure.Tests;

public sealed class RendererTests
{
    [Fact]
    public async Task ProposalRendererProducesValidPngWithExpectedDimensions()
    {
        var renderer = new ProposalSummaryRenderer();

        var rendered = await renderer.RenderAsync(
            "{\"propostas\":[{\"cliente\":\"Ana\",\"produto\":\"Plano\",\"valor\":1234.56,\"status\":\"Aprovada\"}]}",
            CancellationToken.None);
        await using var content = rendered.Content;
        var header = new byte[8];
        var bytesRead = await content.ReadAsync(header, CancellationToken.None);

        Assert.Equal(8, bytesRead);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], header);
        Assert.Equal("image/png", rendered.MimeType);
        Assert.Equal(1200, rendered.Width);
        Assert.True(rendered.Height > 300);
    }

    [Fact]
    public async Task VehicleRendererSupportsEmptyListAndRejectsMissingArray()
    {
        var renderer = new VehicleSummaryRenderer();

        var empty = await renderer.RenderAsync("{\"veiculos\":[]}", CancellationToken.None);
        await empty.Content.DisposeAsync();
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            renderer.RenderAsync("{}", CancellationToken.None));

        Assert.Equal("veiculos.png", empty.FileName);
        Assert.True(empty.Height > 300);
        Assert.Contains("veiculos", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolverIsCaseInsensitiveAndReportsUnknownRenderer()
    {
        IContentRenderer proposal = new ProposalSummaryRenderer();
        var resolver = new ContentRendererResolver([proposal, new VehicleSummaryRenderer()]);

        Assert.Same(proposal, resolver.Resolve("PROPOSAL-SUMMARY"));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve("missing"));
    }
}
