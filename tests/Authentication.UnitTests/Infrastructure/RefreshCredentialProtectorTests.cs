using Authentication.Infrastructure.Sessions;
using Xunit;

namespace Authentication.UnitTests.Infrastructure;

public sealed class RefreshCredentialProtectorTests
{
    [Fact]
    public void CreatesOpaque256BitCredentialAndOnlyExposesDigest()
    {
        using var protector = new RefreshCredentialProtector();
        var raw = protector.CreateRawCredential();
        Assert.Equal(43, raw.Length);
        Assert.True(RefreshCredentialProtector.TryHash(raw, out var digest));
        Assert.Equal(32, digest.Length);
        Assert.NotEqual(raw, Convert.ToBase64String(digest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64url")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa=")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]
    public void RejectsMalformedCredentials(string value) => Assert.False(RefreshCredentialProtector.TryHash(value, out _));
}
