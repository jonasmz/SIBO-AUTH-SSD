using Authentication.Infrastructure.Security;
using Xunit;

namespace Authentication.UnitTests.Infrastructure;

public sealed class JwtOptionsTests
{
    [Fact]
    public void HasTheRequiredFifteenMinuteDefaultLifetime()
    {
        var options = new JwtOptions();

        Assert.Equal(15, options.AccessTokenLifetimeMinutes);
    }
}
