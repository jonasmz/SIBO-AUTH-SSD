using System.Net;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class DevelopmentStartupTests
{
    [Fact]
    public async Task TheHostStartsInDevelopmentWhereServiceLifetimesAreValidated()
    {
        // Development enables DI scope validation: a singleton that captures a scoped service fails here even
        // though Testing and Production start, so this guards every registration against captive dependencies.
        using var factory = new AuthenticationApiFactory(environment: "Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheRecoveryAddressLimitStillNormalizesTheAddressInDevelopment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(
            environment: "Development",
            additionalSettings: new Dictionary<string, string> { ["RateLimiting__ForgotPasswordAddress__PermitLimit"] = "1" });
        using var client = factory.CreateClient();

        using var first = await ForgotAsync(client, "Someone@Example.Test", cancellationToken);
        using var second = await ForgotAsync(client, "someone@example.test", cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email, CancellationToken cancellationToken) =>
        client.PostAsync(
            "/api/auth/forgot-password",
            new StringContent(System.Text.Json.JsonSerializer.Serialize(new { email }), System.Text.Encoding.UTF8, "application/json"),
            cancellationToken);
}
