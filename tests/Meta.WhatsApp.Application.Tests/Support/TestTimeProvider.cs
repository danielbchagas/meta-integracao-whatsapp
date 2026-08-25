namespace Meta.WhatsApp.Application.Tests.Support;

internal sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);

    public void SetUtcNow(DateTimeOffset value) => UtcNow = value;
}
