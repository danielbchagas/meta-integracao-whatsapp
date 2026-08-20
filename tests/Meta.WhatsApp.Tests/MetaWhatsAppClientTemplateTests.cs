using System.Text.Json;
using Meta.WhatsApp.Templates;

namespace Meta.WhatsApp.Tests;

public sealed class MetaWhatsAppClientTemplateTests
{
    private static readonly TemplateDefinition DesiredTemplate = new()
    {
        Name = "pedido_confirmado",
        Language = "pt_BR",
        Category = "utility",
        Components =
        [
            new TemplateComponent
            {
                Type = "BODY",
                Text = "Olá {{1}}, seu pedido foi confirmado."
            }
        ]
    };

    [Fact]
    public async Task EnsureTemplateAsync_CreatesTemplateWhenItDoesNotExist()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson("{\"data\":[]}");
        handler.EnqueueJson("{\"id\":\"new-id\",\"status\":\"PENDING\",\"category\":\"UTILITY\"}");
        var client = CreateClient(handler);

        var result = await client.EnsureTemplateAsync(DesiredTemplate);

        Assert.Equal(TemplateSynchronizationAction.Created, result.Action);
        Assert.Equal("new-id", result.TemplateId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("/waba-id/message_templates?", handler.Requests[0].Uri.ToString());
        Assert.Contains("name=pedido_confirmado", handler.Requests[0].Uri.Query);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);

        using var json = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("UTILITY", json.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public async Task EnsureTemplateAsync_DoesNotUpdateMatchingTemplate()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ExistingTemplateResponse("Olá {{1}}, seu pedido foi confirmado."));
        var client = CreateClient(handler);

        var result = await client.EnsureTemplateAsync(DesiredTemplate);

        Assert.Equal(TemplateSynchronizationAction.Unchanged, result.Action);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EnsureTemplateAsync_UpdatesTemplateOnlyWhenContentChanged()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(ExistingTemplateResponse("Conteúdo antigo"));
        handler.EnqueueJson("{\"success\":true}");
        var client = CreateClient(handler);

        var result = await client.EnsureTemplateAsync(DesiredTemplate);

        Assert.Equal(TemplateSynchronizationAction.Updated, result.Action);
        Assert.Equal("template-id", result.TemplateId);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://graph.facebook.com/v23.0/template-id", handler.Requests[1].Uri.ToString());

        using var json = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal(
            "Olá {{1}}, seu pedido foi confirmado.",
            json.RootElement.GetProperty("components")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task GetTemplatesAsync_FollowsCursorPagination()
    {
        var handler = new TestHttpMessageHandler();
        handler.EnqueueJson(
            """
            {
              "data": [{
                "id":"one", "name":"first", "language":"pt_BR",
                "category":"UTILITY", "status":"APPROVED", "components":[]
              }],
              "paging": {
                "cursors": { "after":"cursor-2" },
                "next":"https://graph.facebook.com/v23.0/waba-id/message_templates?after=cursor-2"
              }
            }
            """);
        handler.EnqueueJson(
            """
            {
              "data": [{
                "id":"two", "name":"second", "language":"pt_BR",
                "category":"MARKETING", "status":"PENDING", "components":[]
              }]
            }
            """);
        var client = CreateClient(handler);

        var templates = await client.GetTemplatesAsync();

        Assert.Equal(2, templates.Count);
        Assert.Contains("after=cursor-2", handler.Requests[1].Uri.Query);
    }

    private static MetaWhatsAppClient CreateClient(TestHttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            new MetaWhatsAppOptions
            {
                AccessToken = "access-token",
                PhoneNumberId = "phone-id",
                BusinessAccountId = "waba-id",
                GraphApiVersion = "v23.0"
            });

    private static string ExistingTemplateResponse(string bodyText) =>
        $$"""
        {
          "data": [{
            "id": "template-id",
            "name": "pedido_confirmado",
            "language": "pt_BR",
            "category": "UTILITY",
            "status": "APPROVED",
            "components": [{
              "type": "BODY",
              "text": {{JsonSerializer.Serialize(bodyText)}}
            }]
          }]
        }
        """;
}
