namespace Meta.WhatsApp.Specs.Support;

internal sealed class SpecTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
}
