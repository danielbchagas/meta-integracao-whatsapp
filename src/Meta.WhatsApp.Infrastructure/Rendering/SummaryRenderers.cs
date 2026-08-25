using System.Globalization;
using System.Text.Json;
using SkiaSharp;
using Meta.WhatsApp.Application.Abstractions;

namespace Meta.WhatsApp.Infrastructure.Rendering;

public sealed class ContentRendererResolver(IEnumerable<IContentRenderer> renderers) : IContentRendererResolver
{
    private readonly Dictionary<string, IContentRenderer> _renderers = renderers
        .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

    public IContentRenderer Resolve(string name) =>
        _renderers.TryGetValue(name, out var renderer)
            ? renderer
            : throw new InvalidOperationException($"Renderer '{name}' is not registered.");
}

public sealed class ProposalSummaryRenderer : IContentRenderer
{
    public string Name => "proposal-summary";

    public string Version => "1.0.0";

    public Task<RenderedContent> RenderAsync(string normalizedPayload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(normalizedPayload);
        var rows = ReadRows(document.RootElement, "propostas", "proposals");
        return Task.FromResult(RenderTable(
            "Resumo de propostas",
            ["Cliente", "Produto", "Valor", "Status"],
            rows.Select(item => new[]
            {
                GetText(item, "cliente", "customer"),
                GetText(item, "produto", "product"),
                GetCurrency(item, "valor", "value"),
                GetText(item, "status")
            }).ToArray(),
            "propostas"));
    }

    private static JsonElement[] ReadRows(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                return rows.EnumerateArray().Select(item => item.Clone()).ToArray();
            }
        }

        throw new ArgumentException("Proposal summary requires a 'propostas' array.", nameof(root));
    }

    internal static RenderedContent RenderTable(
        string title,
        IReadOnlyList<string> headers,
        IReadOnlyList<string[]> allRows,
        string filePrefix)
    {
        const int width = 1200;
        const int headerHeight = 220;
        const int columnHeaderHeight = 70;
        const int rowHeight = 74;
        const int footerHeight = 90;
        const int maxRows = 15;
        var visibleRows = allRows.Take(maxRows).ToArray();
        var height = headerHeight + columnHeaderHeight + (Math.Max(1, visibleRows.Length) * rowHeight) + footerHeight;

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var titleFont = new SKFont(SKTypeface.Default, 48);
        using var headerFont = new SKFont(SKTypeface.Default, 25);
        using var rowFont = new SKFont(SKTypeface.Default, 25);
        using var smallFont = new SKFont(SKTypeface.Default, 21);
        using var paint = new SKPaint { IsAntialias = true };

        canvas.Clear(new SKColor(246, 248, 252));
        paint.Color = new SKColor(19, 47, 76);
        canvas.DrawRect(0, 0, width, headerHeight, paint);
        paint.Color = SKColors.White;
        canvas.DrawText(title, 64, 98, titleFont, paint);
        paint.Color = new SKColor(192, 216, 238);
        canvas.DrawText(
            string.Create(CultureInfo.InvariantCulture, $"{allRows.Count} item(ns)"),
            66,
            154,
            smallFont,
            paint);

        var columnWidths = GetColumnWidths(headers.Count, width - 128);
        var tableLeft = 64;
        var y = headerHeight;
        paint.Color = new SKColor(223, 232, 242);
        canvas.DrawRect(tableLeft, y, width - 128, columnHeaderHeight, paint);
        DrawCells(canvas, paint, headerFont, headers, columnWidths, tableLeft, y + 45, SKColors.DarkSlateGray);
        y += columnHeaderHeight;

        if (visibleRows.Length == 0)
        {
            paint.Color = SKColors.White;
            canvas.DrawRect(tableLeft, y, width - 128, rowHeight, paint);
            paint.Color = SKColors.Gray;
            canvas.DrawText("Nenhum item para exibir", tableLeft + 20, y + 48, rowFont, paint);
            y += rowHeight;
        }
        else
        {
            for (var index = 0; index < visibleRows.Length; index++)
            {
                paint.Color = index % 2 == 0 ? SKColors.White : new SKColor(239, 244, 249);
                canvas.DrawRect(tableLeft, y, width - 128, rowHeight, paint);
                DrawCells(
                    canvas,
                    paint,
                    rowFont,
                    visibleRows[index],
                    columnWidths,
                    tableLeft,
                    y + 48,
                    new SKColor(35, 48, 62));
                y += rowHeight;
            }
        }

        paint.Color = new SKColor(94, 111, 128);
        var footer = allRows.Count > maxRows
            ? string.Create(CultureInfo.InvariantCulture, $"Exibindo {maxRows} de {allRows.Count} itens")
            : "Gerado pelo serviço de mensagens";
        canvas.DrawText(footer, tableLeft, height - 34, smallFont, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 92)
            ?? throw new InvalidOperationException("SkiaSharp failed to encode the rendered summary.");
        var stream = new MemoryStream(encoded.ToArray(), writable: false);
        return new RenderedContent(stream, $"{filePrefix}.png", "image/png", width, height);
    }

    private static int[] GetColumnWidths(int count, int availableWidth)
    {
        if (count == 4)
        {
            return [330, 310, 220, availableWidth - 860];
        }

        var width = availableWidth / count;
        return Enumerable.Repeat(width, count).ToArray();
    }

    private static void DrawCells(
        SKCanvas canvas,
        SKPaint paint,
        SKFont font,
        IReadOnlyList<string> cells,
        int[] widths,
        float left,
        float baseline,
        SKColor color)
    {
        paint.Color = color;
        var x = left + 16;
        for (var index = 0; index < cells.Count; index++)
        {
            var maxCharacters = Math.Max(4, (widths[index] - 30) / 14);
            var text = Ellipsize(cells[index], maxCharacters);
            canvas.DrawText(text, x, baseline, font, paint);
            x += widths[index];
        }
    }

    private static string Ellipsize(string value, int maxCharacters) =>
        value.Length <= maxCharacters ? value : string.Concat(value.AsSpan(0, maxCharacters - 1), "…");

    private static string GetText(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
            }
        }

        return "—";
    }

    private static string GetCurrency(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.TryGetDecimal(out var amount))
            {
                return amount.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
            }
        }

        return "—";
    }
}

public sealed class VehicleSummaryRenderer : IContentRenderer
{
    public string Name => "vehicle-summary";

    public string Version => "1.0.0";

    public Task<RenderedContent> RenderAsync(string normalizedPayload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(normalizedPayload);
        if (!document.RootElement.TryGetProperty("veiculos", out var vehicles) ||
            vehicles.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Vehicle summary requires a 'veiculos' array.", nameof(normalizedPayload));
        }

        var rows = vehicles.EnumerateArray().Select(item => new[]
        {
            Read(item, "marca"),
            Read(item, "modelo"),
            Read(item, "ano"),
            Read(item, "placa")
        }).ToArray();
        return Task.FromResult(ProposalSummaryRenderer.RenderTable(
            "Resumo de veículos",
            ["Marca", "Modelo", "Ano", "Placa"],
            rows,
            "veiculos"));
    }

    private static string Read(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) ? value.ToString() : "—";
}
