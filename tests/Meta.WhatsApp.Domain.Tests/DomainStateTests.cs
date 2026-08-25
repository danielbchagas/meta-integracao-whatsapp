using Meta.WhatsApp.Domain;

namespace Meta.WhatsApp.Domain.Tests;

public sealed class DomainStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WabaSynchronizeActivatesAggregateWithoutStoringToken()
    {
        var waba = Waba.Register("123456", "meta-prod", Now);

        waba.Synchronize("business-1", "Minha WABA", "meta-prod-v2", Now.AddMinutes(1));

        Assert.Equal(WabaStatus.Active, waba.Status);
        Assert.Equal("meta-prod-v2", waba.CredentialKey);
        Assert.Equal(Now.AddMinutes(1), waba.LastSyncAt);
        Assert.DoesNotContain("token", waba.CredentialKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WabaRejectsNonFailureStatusWhenMarkingSyncFailure()
    {
        var waba = Waba.Register("123456", "meta-prod", Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => waba.MarkSyncFailure(WabaStatus.Active, Now));
    }

    [Fact]
    public void TemplateCreatesVersionOnlyWhenComponentsHashChanges()
    {
        var template = new MessageTemplate(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "meta-template",
            "pedido_aprovado",
            "pt_BR",
            "utility",
            "approved",
            "[{\"type\":\"BODY\"}]",
            "HASH-1",
            Now);

        var unchanged = template.ApplySnapshot(
            "meta-template",
            "utility",
            "approved",
            "[{\"type\":\"BODY\"}]",
            "HASH-1",
            Now.AddMinutes(1));
        var changed = template.ApplySnapshot(
            "meta-template",
            "utility",
            "pending",
            "[{\"type\":\"BODY\",\"text\":\"Novo\"}]",
            "HASH-2",
            Now.AddMinutes(2));

        Assert.False(unchanged);
        Assert.True(changed);
        Assert.Equal(2, template.CurrentVersion);
        Assert.Equal(2, template.Versions.Count);
        Assert.Equal("PENDING", template.Status);
    }

    [Theory]
    [InlineData(ContentStrategy.TextTemplate, null, null)]
    [InlineData(ContentStrategy.ImageTemplate, "resumo", null)]
    public void MessageDefinitionValidatesStrategyRequirements(
        ContentStrategy strategy,
        string? templateName,
        string? renderer)
    {
        Assert.Throws<ArgumentException>(() => new MessageDefinition(
            "TIPO",
            strategy,
            templateName,
            "pt_BR",
            renderer,
            enabled: true,
            Now));
    }

    [Fact]
    public void MessageLifecycleTracksMetaMilestonesAndRejectsInvalidTransition()
    {
        var message = CreateMessage(ContentStrategy.FreeText);

        message.TransitionTo(WhatsAppMessageStatus.Queued, Now.AddSeconds(1));
        message.TransitionTo(WhatsAppMessageStatus.Sending, Now.AddSeconds(2));
        message.MarkSent("wamid.1", Now.AddSeconds(3));
        message.MarkDelivered(Now.AddSeconds(4));
        message.MarkRead(Now.AddSeconds(5));

        Assert.Equal(WhatsAppMessageStatus.Read, message.Status);
        Assert.Equal("wamid.1", message.MetaMessageId);
        Assert.Equal(Now.AddSeconds(3), message.SentAt);
        Assert.Equal(Now.AddSeconds(4), message.DeliveredAt);
        Assert.Equal(Now.AddSeconds(5), message.ReadAt);
        Assert.Throws<InvalidOperationException>(() =>
            message.TransitionTo(WhatsAppMessageStatus.Queued, Now.AddSeconds(6)));
    }

    [Fact]
    public void MessageNormalizesRecipientAndValidatesLength()
    {
        var message = new WhatsAppMessage(
            Guid.NewGuid(),
            "key",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "+55 (11) 99999-0000",
            "aviso",
            ContentStrategy.FreeText,
            "{\"text\":\"Oi\"}",
            null,
            Now,
            Guid.NewGuid());

        Assert.Equal("5511999990000", message.Recipient);
        Assert.Throws<ArgumentException>(() => new WhatsAppMessage(
            Guid.NewGuid(),
            "key-2",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "123",
            "aviso",
            ContentStrategy.FreeText,
            "{}",
            null,
            Now,
            Guid.NewGuid()));
    }

    [Fact]
    public void OperationCanRetryAndThenComplete()
    {
        var operation = new IntegrationOperation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OperationType.SendMessage,
            Guid.NewGuid(),
            Now,
            Guid.NewGuid());

        operation.Start(Now);
        operation.Fail("429", Now.AddMinutes(1), permanent: false, Now);
        operation.Start(Now.AddMinutes(1));
        operation.Complete(Now.AddMinutes(2));

        Assert.Equal(2, operation.Attempts);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal(Now.AddMinutes(2), operation.CompletedAt);
    }

    private static WhatsAppMessage CreateMessage(ContentStrategy strategy) => new(
        Guid.NewGuid(),
        "business-key",
        Guid.NewGuid(),
        Guid.NewGuid(),
        "5511999990000",
        "AVISO_SIMPLES",
        strategy,
        "{\"text\":\"Olá\"}",
        null,
        Now,
        Guid.NewGuid());
}
