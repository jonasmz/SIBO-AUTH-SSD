namespace Authentication.IntegrationTests.Infrastructure;

public sealed class ControlledTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
