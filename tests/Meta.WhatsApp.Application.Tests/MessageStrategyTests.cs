using System.Text.Json;
using Meta.WhatsApp.Application;
using Meta.WhatsApp.Application.Messages;
using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Application.Tests;

public sealed class MessageStrategyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ResolverSelectsEachContentStrategy()
    {
        var resolver = CreateResolver();

        Assert.IsType<FreeTextContentStrategy>(resolver.Resolve(ContentStrategy.FreeText));
        Assert.IsType<TextTemplateContentStrategy>(resolver.Resolve(ContentStrategy.TextTemplate));
        Assert.IsType<ImageTemplateContentStrategy>(resolver.Resolve(ContentStrategy.ImageTemplate));
    }

    [Fact]
    public void ResolverRejectsMissingStrategy()
    {
        var resolver = new MessageContentStrategyResolver([new FreeTextContentStrategy()]);

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(ContentStrategy.ImageTemplate));
    }

    [Fact]
    public void FreeTextStrategyBuildsMetaPayload()
    {
        var definition = Definition(ContentStrategy.FreeText);
        var message = Message(ContentStrategy.FreeText, "{\"previewUrl\":true,\"text\":\"Olá\"}");

        var prepared = new FreeTextContentStrategy().Prepare(definition, message, null);
        var json = JsonSerializer.Serialize(prepared.Content, ApplicationJson.SerializerOptions);

        Assert.Equal("text", prepared.Type);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Olá", document.RootElement.GetProperty("body").GetString());
        Assert.True(document.RootElement.GetProperty("preview_url").GetBoolean());
    }

    [Fact]
    public void TextTemplateStrategyBindsBodyParameters()
    {
        var definition = Definition(ContentStrategy.TextTemplate);
        var message = Message(ContentStrategy.TextTemplate, "{\"parameters\":[\"Daniel\",123,true]}");

        var prepared = new TextTemplateContentStrategy().Prepare(definition, message, null);
        var json = JsonSerializer.Serialize(prepared.Content, ApplicationJson.SerializerOptions);

        Assert.Equal("template", prepared.Type);
        Assert.Contains("\"name\":\"template_teste\"", json, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"Daniel\"", json, StringComparison.Ordinal);
        Assert.Contains("\"text\":\"123\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageTemplateStrategyRequiresMediaAndAddsHeader()
    {
        var definition = Definition(ContentStrategy.ImageTemplate);
        var message = Message(ContentStrategy.ImageTemplate, "{\"parameters\":[\"Daniel\"]}");
        var strategy = new ImageTemplateContentStrategy();

        Assert.Throws<InvalidOperationException>(() => strategy.Prepare(definition, message, null));

        var media = new RenderedMedia(
            Guid.NewGuid(),
            message.Id,
            "path/image.png",
            "image/png",
            new string('A', 64),
            100,
            1200,
            600,
            "proposal-summary",
            "1",
            Now,
            null);
        media.SetMetaMediaId("media-123");
        var prepared = strategy.Prepare(definition, message, media);
        var json = JsonSerializer.Serialize(prepared.Content, ApplicationJson.SerializerOptions);

        Assert.Contains("\"type\":\"header\"", json, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"media-123\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalizerProducesSameHashInputForDifferentPropertyOrder()
    {
        var first = JsonCanonicalizer.Canonicalize("{\"b\":2,\"a\":{\"d\":4,\"c\":3}}");
        var second = JsonCanonicalizer.Canonicalize("{\"a\":{\"c\":3,\"d\":4},\"b\":2}");

        Assert.Equal("{\"a\":{\"c\":3,\"d\":4},\"b\":2}", first);
        Assert.Equal(first, second);
    }

    private static MessageContentStrategyResolver CreateResolver() => new(
    [
        new FreeTextContentStrategy(),
        new TextTemplateContentStrategy(),
        new ImageTemplateContentStrategy()
    ]);

    private static MessageDefinition Definition(ContentStrategy strategy) => new(
        "TIPO",
        strategy,
        strategy == ContentStrategy.FreeText ? null : "template_teste",
        "pt_BR",
        strategy == ContentStrategy.ImageTemplate ? "proposal-summary" : null,
        enabled: true,
        Now);

    private static WhatsAppMessage Message(ContentStrategy strategy, string payload) => new(
        Guid.NewGuid(),
        Guid.NewGuid().ToString("N"),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "5511999990000",
        "TIPO",
        strategy,
        payload,
        strategy == ContentStrategy.FreeText ? null : "template_teste",
        Now,
        Guid.NewGuid());
}
