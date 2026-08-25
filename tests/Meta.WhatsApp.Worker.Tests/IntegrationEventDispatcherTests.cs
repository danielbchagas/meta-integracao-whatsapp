using System.Text.Json;
using Meta.WhatsApp.Application;

namespace Meta.WhatsApp.Worker.Tests;

public sealed class IntegrationEventDispatcherTests
{
    [Theory]
    [InlineData(IntegrationEventTypes.WabaSynchronized)]
    [InlineData(IntegrationEventTypes.MessageStatusChanged)]
    [InlineData(IntegrationEventTypes.InboundMessageReceived)]
    public async Task TerminalEventsAreAcknowledgedWithoutAdditionalProcessing(string eventType)
    {
        var dispatcher = new IntegrationEventDispatcher(null!, null!, null!);

        await dispatcher.DispatchAsync(eventType, "{}", Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public async Task UnsupportedEventIsRejectedForRetryOrDeadLetterHandling()
    {
        var dispatcher = new IntegrationEventDispatcher(null!, null!, null!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync("unsupported.v1", "{}", Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("unsupported.v1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedKnownEventIsRejectedBeforeAProcessorRuns()
    {
        var dispatcher = new IntegrationEventDispatcher(null!, null!, null!);

        await Assert.ThrowsAsync<JsonException>(() => dispatcher.DispatchAsync(
            IntegrationEventTypes.MessageRequested,
            "null",
            Guid.NewGuid(),
            CancellationToken.None));
    }
}
