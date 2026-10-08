using Authentication.Domain.Sessions;
using Xunit;

namespace Authentication.UnitTests.Domain;

public sealed class RefreshCredentialTests
{
    [Fact]
    public void CredentialCanOnlyBeConsumedOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var credential = new RefreshCredential("credential", "family", new byte[32], now, now.AddDays(7));
        Assert.True(credential.Consume(now, "replacement"));
        Assert.False(credential.Consume(now, "another"));
    }
}
