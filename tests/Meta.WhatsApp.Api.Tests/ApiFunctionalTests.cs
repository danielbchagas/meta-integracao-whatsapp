using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meta.WhatsApp.Api.Tests.Support;

namespace Meta.WhatsApp.Api.Tests;

public sealed class ApiFunctionalTests
{
    [Fact]
    public async Task WabaResourcesAndAvailableTemplatesExposeSynchronizedState()
    {
        await using var factory = new WhatsAppApiFactory();
        using var client = factory.CreateClient();
        await RegisterWabaAsync(client);

        using var wabaResponse = await client.GetAsync("/api/wabas/waba-123");
        using var phonesResponse = await client.GetAsync("/api/wabas/waba-123/phone-numbers");
        using var templatesResponse = await client.GetAsync("/api/wabas/waba-123/templates");
        using var availableResponse = await client.GetAsync("/api/wabas/waba-123/templates/available");

        Assert.Equal(HttpStatusCode.OK, wabaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, phonesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, templatesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, availableResponse.StatusCode);
        using var waba = await JsonDocument.ParseAsync(await wabaResponse.Content.ReadAsStreamAsync());
        using var phones = await JsonDocument.ParseAsync(await phonesResponse.Content.ReadAsStreamAsync());
        using var templates = await JsonDocument.ParseAsync(await templatesResponse.Content.ReadAsStreamAsync());
        using var available = await JsonDocument.ParseAsync(await availableResponse.Content.ReadAsStreamAsync());
        Assert.Equal("Active", waba.RootElement.GetProperty("status").GetString());
        Assert.Equal("phone-1", phones.RootElement[0].GetProperty("metaPhoneNumberId").GetString());
        Assert.Equal(2, templates.RootElement.GetArrayLength());
        var approved = Assert.Single(available.RootElement.EnumerateArray());
        Assert.Equal("proposta_aprovada", approved.GetProperty("name").GetString());
        Assert.Equal("PROPOSTA_APROVADA", approved.GetProperty("messageTypes")[0].GetString());
    }

    [Fact]
    public async Task MissingResourcesAndInvalidMessageRequestsReturnPreciseStatusCodes()
    {
        await using var factory = new WhatsAppApiFactory();
        using var client = factory.CreateClient();

        using var missingWaba = await client.GetAsync("/api/wabas/missing");
        using var missingTemplates = await client.GetAsync("/api/wabas/missing/templates/available");
        using var missingOperation = await client.GetAsync($"/api/operations/{Guid.NewGuid():D}");
        using var missingMessage = await client.GetAsync($"/api/messages/{Guid.NewGuid():D}");
        using var noIdempotency = await client.PostAsJsonAsync("/api/messages", new
        {
            wabaId = "missing",
            phoneNumberId = "missing",
            messageType = "AVISO_SIMPLES",
            recipient = "5511999990000",
            data = new { text = "Olá" }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages")
        {
            Content = JsonContent.Create(new
            {
                wabaId = "missing",
                phoneNumberId = "missing",
                messageType = "AVISO_SIMPLES",
                recipient = "5511999990000",
                data = new { text = "Olá" }
            })
        };
        request.Headers.Add("Idempotency-Key", "missing:1");
        using var unknownWaba = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, missingWaba.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingTemplates.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingOperation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingMessage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noIdempotency.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownWaba.StatusCode);
    }

    [Fact]
    public async Task TemplateMutationsAreQueuedAndOperationsCanBeQueried()
    {
        await using var factory = new WhatsAppApiFactory();
        using var client = factory.CreateClient();
        await RegisterWabaAsync(client);
        using var templates = await client.GetAsync("/api/wabas/waba-123/templates");
        using var templateJson = await JsonDocument.ParseAsync(await templates.Content.ReadAsStreamAsync());
        var templateId = templateJson.RootElement[0].GetProperty("id").GetGuid();
        var templateRequest = new
        {
            name = "novo_template",
            language = "pt_BR",
            category = "UTILITY",
            components = new[] { new { type = "BODY", text = "Olá {{1}}" } }
        };

        using var create = await client.PostAsJsonAsync("/api/wabas/waba-123/templates", templateRequest);
        using var update = await client.PutAsJsonAsync(
            $"/api/wabas/waba-123/templates/{templateId:D}",
            templateRequest);
        using var delete = await client.DeleteAsync($"/api/wabas/waba-123/templates/{templateId:D}");
        using var createJson = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var operationId = createJson.RootElement.GetProperty("operationId").GetGuid();
        using var operation = await client.GetAsync($"/api/operations/{operationId:D}");

        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, update.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, delete.StatusCode);
        Assert.Equal(HttpStatusCode.OK, operation.StatusCode);
        Assert.Equal($"/api/operations/{operationId:D}", create.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task MessageEndpointUsesIdempotencyKeyAndReturnsOriginalMessageOnDuplicate()
    {
        await using var factory = new WhatsAppApiFactory();
        using var client = factory.CreateClient();
        await RegisterWabaAsync(client);
        var firstRequest = CreateMessageRequest("message:1");
        var duplicateRequest = CreateMessageRequest("message:1");

        using var first = await client.SendAsync(firstRequest);
        using var duplicate = await client.SendAsync(duplicateRequest);
        using var firstJson = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        using var duplicateJson = await JsonDocument.ParseAsync(await duplicate.Content.ReadAsStreamAsync());
        var messageId = firstJson.RootElement.GetProperty("messageId").GetGuid();
        using var get = await client.GetAsync($"/api/messages/{messageId:D}");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        Assert.False(firstJson.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.True(duplicateJson.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.Equal(messageId, duplicateJson.RootElement.GetProperty("messageId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task WebhookChallengeAcceptsOnlyConfiguredToken()
    {
        await using var factory = new WhatsAppApiFactory();
        using var client = factory.CreateClient();

        using var accepted = await client.GetAsync(
            "/webhooks/meta?hub.mode=subscribe&hub.verify_token=verify-token&hub.challenge=challenge-value");
        using var denied = await client.GetAsync(
            "/webhooks/meta?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=challenge-value");

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("challenge-value", await accepted.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task WebhookAuthenticatesDeduplicatesAndRejectsInvalidOrMalformedPayloads()
    {
        await using var factory = new WhatsAppApiFactory();
        using var client = factory.CreateClient();
        var payload = Encoding.UTF8.GetBytes(
            "{\"object\":\"whatsapp_business_account\",\"entry\":[{\"changes\":[{\"field\":\"messages\",\"value\":{\"metadata\":{\"phone_number_id\":\"phone-1\"},\"messages\":[{\"from\":\"5511999990000\",\"id\":\"wamid.inbound\",\"timestamp\":\"1787592000\",\"type\":\"text\",\"text\":{\"body\":\"Olá\"}}]}}]}]}");

        using var accepted = await SendWebhookAsync(client, payload, Signature(payload));
        using var duplicate = await SendWebhookAsync(client, payload, Signature(payload));
        using var unauthorized = await SendWebhookAsync(client, payload, "sha256=00");
        var malformed = Encoding.UTF8.GetBytes("{\"object\":\"invalid\",\"entry\":[]}");
        using var invalid = await SendWebhookAsync(client, malformed, Signature(malformed));
        using var acceptedJson = await JsonDocument.ParseAsync(await accepted.Content.ReadAsStreamAsync());
        using var duplicateJson = await JsonDocument.ParseAsync(await duplicate.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(1, acceptedJson.RootElement.GetProperty("accepted").GetInt32());
        Assert.Equal(1, duplicateJson.RootElement.GetProperty("duplicates").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task HealthEndpointsAreAvailableWithoutCallingMeta()
    {
        await using var factory = new WhatsAppApiFactory();
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    private static async Task RegisterWabaAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/wabas", new
        {
            wabaId = "waba-123",
            credentialKey = "meta-prod"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static HttpRequestMessage CreateMessageRequest(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/messages")
        {
            Content = JsonContent.Create(new
            {
                wabaId = "waba-123",
                phoneNumberId = "phone-1",
                messageType = "AVISO_SIMPLES",
                recipient = "5511999990000",
                data = new { text = "Olá" }
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        byte[] payload,
        string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/meta")
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", signature);
        return await client.SendAsync(request);
    }

    private static string Signature(byte[] payload) =>
        $"sha256={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("app-secret"), payload)).ToLowerInvariant()}";
}
