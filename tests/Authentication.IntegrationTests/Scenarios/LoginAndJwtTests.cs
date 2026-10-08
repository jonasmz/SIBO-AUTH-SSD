using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class LoginAndJwtTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAtUtc);

    [Fact]
    public async Task ValidLoginIssuesVerifiableRs256TokenWithDefaultLifetime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(timeProvider: new ControlledTimeProvider(Now));
        using var client = factory.CreateClient();

        var first = await LoginAsync(client, "admin@local.invalid", "admin", cancellationToken);
        var second = await LoginAsync(client, "ADMIN@Local.Invalid", "admin", cancellationToken);

        var expectedIssuedAt = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var expectedExpiry = expectedIssuedAt.AddSeconds(900);

        var firstPayload = await ValidateAsync(factory, first.AccessToken);
        var secondPayload = await ValidateAsync(factory, second.AccessToken);

        Assert.Equal(DateTimeKind.Utc, first.ExpiresAtUtc.Kind);
        Assert.Equal(expectedExpiry.UtcDateTime, first.ExpiresAtUtc);

        Assert.Equal(DatabaseInitializer.AdministratorUserId, firstPayload.GetProperty("sub").GetString());
        Assert.Equal(DatabaseInitializer.AdministratorUserId, secondPayload.GetProperty("sub").GetString());
        Assert.Equal("admin@local.invalid", firstPayload.GetProperty("email").GetString());
        Assert.Equal("Administrator", firstPayload.GetProperty("role").GetString());
        Assert.Equal("https://auth-api.test", firstPayload.GetProperty("iss").GetString());
        Assert.Equal("authentication-api-tests", firstPayload.GetProperty("aud").GetString());
        Assert.Equal(expectedIssuedAt.ToUnixTimeSeconds(), firstPayload.GetProperty("iat").GetInt64());
        Assert.Equal(expectedExpiry.ToUnixTimeSeconds(), firstPayload.GetProperty("exp").GetInt64());
        Assert.Equal(900, firstPayload.GetProperty("exp").GetInt64() - firstPayload.GetProperty("iat").GetInt64());
        Assert.NotEqual(
            firstPayload.GetProperty("jti").GetString(),
            secondPayload.GetProperty("jti").GetString());
    }

    [Fact]
    public async Task ConfiguredLifetimeDeterminesExpiry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(
            timeProvider: new ControlledTimeProvider(Now),
            accessTokenLifetimeMinutes: 5);
        using var client = factory.CreateClient();

        var body = await LoginAsync(client, "admin@local.invalid", "admin", cancellationToken);
        var payload = await ValidateAsync(factory, body.AccessToken);

        Assert.Equal(300, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64()).UtcDateTime,
            body.ExpiresAtUtc);
    }

    [Fact]
    public async Task UnknownEmailAndWrongPasswordReturnIdenticalGeneric401AndIdentityCountsFailures()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        using var unknown = await PostLoginAsync(client, "nobody@local.invalid", "admin", cancellationToken);
        using var wrong = await PostLoginAsync(client, "admin@local.invalid", "not-the-password", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("application/problem+json", wrong.Content.Headers.ContentType?.MediaType);

        var unknownBody = await unknown.Content.ReadAsStringAsync(cancellationToken);
        var wrongBody = await wrong.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(StripTraceId(unknownBody), StripTraceId(wrongBody));
        Assert.Contains("Invalid credentials.", wrongBody, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var admin = await context.Users.SingleAsync(cancellationToken);
        Assert.Equal(1, admin.AccessFailedCount);

        _ = await LoginAsync(client, "admin@local.invalid", "admin", cancellationToken);
        context.ChangeTracker.Clear();
        Assert.Equal(0, (await context.Users.SingleAsync(cancellationToken)).AccessFailedCount);
    }

    [Fact]
    public async Task LockedAccountIsIndistinguishableFromWrongCredentials()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await PostLoginAsync(client, "admin@local.invalid", "wrong", cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var locked = await PostLoginAsync(client, "admin@local.invalid", "admin", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("Invalid credentials.", await locked.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"email":"","password":"admin"}""")]
    [InlineData("""{"email":"admin@local.invalid","password":"  "}""")]
    [InlineData("""{"email":"not-an-email","password":"admin"}""")]
    [InlineData("""{"email":""")]
    public async Task MissingBlankOrMalformedInputReturns400ProblemDetails(string json)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/auth/login", content, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<LoginBody> LoginAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await PostLoginAsync(client, email, password, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginBody>(cancellationToken);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        return body;
    }

    private static Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);

    private static async Task<JsonElement> ValidateAsync(AuthenticationApiFactory factory, string token)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(factory.Resources.PublicKeyPem);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = new RsaSecurityKey(rsa)
            {
                // The RSA instance is disposed per call, so cached signature providers must not outlive it.
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            },
            ValidIssuer = "https://auth-api.test",
            ValidAudience = "authentication-api-tests",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = false
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("RS256", jwt.Alg);

        using var document = JsonDocument.Parse(Base64UrlEncoder.Decode(token.Split('.')[1]));
        return document.RootElement.Clone();
    }

    private static string StripTraceId(string problemJson)
    {
        using var document = JsonDocument.Parse(problemJson);
        return string.Join(
            "|",
            document.RootElement.EnumerateObject()
                .Where(property => property.Name != "traceId")
                .Select(property => $"{property.Name}={property.Value}"));
    }
}
