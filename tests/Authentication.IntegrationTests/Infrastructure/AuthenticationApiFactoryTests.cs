using Xunit;

namespace Authentication.IntegrationTests.Infrastructure;

public sealed class AuthenticationApiFactoryTests
{
    [Fact]
    public async Task StartsWithDisposableExternalConfiguration()
    {
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
