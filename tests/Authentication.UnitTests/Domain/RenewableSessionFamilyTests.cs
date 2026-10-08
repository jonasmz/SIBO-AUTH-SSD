using Authentication.Domain.Sessions;
using Xunit;

namespace Authentication.UnitTests.Domain;

public sealed class RenewableSessionFamilyTests
{
    [Fact]
    public void ActiveStateEndsAtAbsoluteExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var family = new RenewableSessionFamily("family", "user", now, now.AddDays(7));
        Assert.True(family.IsActive(now.AddDays(7).AddTicks(-1)));
        Assert.False(family.IsActive(now.AddDays(7)));
    }

    [Fact]
    public void RevocationIsIrreversible()
    {
        var now = DateTimeOffset.UtcNow;
        var family = new RenewableSessionFamily("family", "user", now, now.AddDays(7));
        family.Revoke(now, SessionRevocationReason.Logout);
        family.Revoke(now.AddMinutes(1), SessionRevocationReason.Replay);
        Assert.False(family.IsActive(now.AddMinutes(1)));
        Assert.Equal(SessionRevocationReason.Logout, family.RevocationReason);
    }
}
