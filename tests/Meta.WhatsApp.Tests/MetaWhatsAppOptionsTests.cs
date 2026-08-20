namespace Meta.WhatsApp.Tests;

public sealed class MetaWhatsAppOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("23.0")]
    [InlineData("v0.0")]
    [InlineData("latest")]
    public void Client_RejectsInvalidGraphApiVersion(string graphApiVersion)
    {
        var options = ValidOptions() with { GraphApiVersion = graphApiVersion };

        Assert.Throws<ArgumentException>(() => CreateClient(options));
    }

    [Fact]
    public void Client_RejectsInsecureGraphApiAddress()
    {
        var options = ValidOptions() with
        {
            GraphApiBaseAddress = new Uri("http://graph.example.com")
        };

        Assert.Throws<ArgumentException>(() => CreateClient(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Client_RejectsInvalidInboundMessageHistory(int historySize)
    {
        var options = ValidOptions() with { MaxInboundMessageHistory = historySize };

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateClient(options));
    }

    [Fact]
    public void Client_RejectsNonPositiveCustomerServiceWindow()
    {
        var options = ValidOptions() with { CustomerServiceWindow = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateClient(options));
    }

    private static TestOptions ValidOptions() => new()
    {
        AccessToken = "access-token",
        PhoneNumberId = "phone-id",
        BusinessAccountId = "waba-id",
        GraphApiVersion = "v23.0"
    };

    private static MetaWhatsAppClient CreateClient(TestOptions source) =>
        new(
            new HttpClient(new TestHttpMessageHandler()),
            new MetaWhatsAppOptions
            {
                AccessToken = source.AccessToken,
                PhoneNumberId = source.PhoneNumberId,
                BusinessAccountId = source.BusinessAccountId,
                GraphApiVersion = source.GraphApiVersion,
                GraphApiBaseAddress = source.GraphApiBaseAddress,
                CustomerServiceWindow = source.CustomerServiceWindow,
                MaxInboundMessageHistory = source.MaxInboundMessageHistory
            });

    private sealed record TestOptions
    {
        public required string AccessToken { get; init; }
        public required string PhoneNumberId { get; init; }
        public required string BusinessAccountId { get; init; }
        public required string GraphApiVersion { get; init; }
        public Uri GraphApiBaseAddress { get; init; } = new("https://graph.facebook.com");
        public TimeSpan CustomerServiceWindow { get; init; } = TimeSpan.FromHours(24);
        public int MaxInboundMessageHistory { get; init; } = 100;
    }
}
