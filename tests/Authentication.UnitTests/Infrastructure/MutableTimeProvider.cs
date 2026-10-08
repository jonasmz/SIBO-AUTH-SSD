namespace Authentication.UnitTests.Infrastructure;

/// <summary>A settable clock, so time-dependent behavior (rotation, retention) is tested without real waits.</summary>
internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void SetUtcNow(DateTimeOffset value) => _now = value;

    public void Advance(TimeSpan delta) => _now += delta;
}
