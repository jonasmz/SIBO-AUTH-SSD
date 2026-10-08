using Authentication.Infrastructure.Sessions;
using Xunit;

namespace Authentication.UnitTests.Infrastructure;

public sealed class RefreshSessionOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void RejectsNonPositiveOrOverflowingLifetime(int lifetimeDays) => Assert.False(RefreshSessionOptions.HasValidLifetime(lifetimeDays));

    [Theory]
    [InlineData("https://frontend.test", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("frontend.test", false)]
    [InlineData("https://frontend.test/path", false)]
    [InlineData("ftp://frontend.test", false)]
    public void ValidatesExternalFrontendOrigin(string origin, bool expected) => Assert.Equal(expected, RefreshSessionOptions.HasValidFrontendOrigin(origin));
}
