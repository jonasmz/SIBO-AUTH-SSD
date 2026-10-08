using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class AdministrativeAccessTests
{
    private const string ProblemJson = "application/problem+json";

    private sealed record Operation(string Name, HttpMethod Method, string Path, Func<string> Body, HttpStatusCode AdministratorStatus);

    /// <summary>The administrative operation list; later stories add their own operations here.</summary>
    private static IReadOnlyList<Operation> Operations(string existingUserId, string existingRoleId) =>
    [
        new("list users", HttpMethod.Get, "/api/admin/users", () => string.Empty, HttpStatusCode.OK),
        new("get user", HttpMethod.Get, $"/api/admin/users/{existingUserId}", () => string.Empty, HttpStatusCode.OK),
        new(
            "create user",
            HttpMethod.Post,
            "/api/admin/users",
            () => JsonSerializer.Serialize(new { email = $"{Guid.NewGuid():N}@example.test", password = "Passw0rd!" }),
            HttpStatusCode.Created),
        new(
            "update user",
            HttpMethod.Patch,
            $"/api/admin/users/{existingUserId}",
            () => JsonSerializer.Serialize(new { email = $"{Guid.NewGuid():N}@example.test" }),
            HttpStatusCode.OK),
        new("enable user", HttpMethod.Post, $"/api/admin/users/{existingUserId}/enable", () => string.Empty, HttpStatusCode.OK),
        new("disable user", HttpMethod.Post, $"/api/admin/users/{existingUserId}/disable", () => string.Empty, HttpStatusCode.OK),
        new("list roles", HttpMethod.Get, "/api/admin/roles", () => string.Empty, HttpStatusCode.OK),
        new(
            "create role",
            HttpMethod.Post,
            "/api/admin/roles",
            () => JsonSerializer.Serialize(new { name = $"Role{Guid.NewGuid():N}" }),
            HttpStatusCode.Created),
        new(
            "rename role",
            HttpMethod.Patch,
            $"/api/admin/roles/{existingRoleId}",
            () => JsonSerializer.Serialize(new { name = $"Role{Guid.NewGuid():N}" }),
            HttpStatusCode.OK),
        new(
            "replace user roles",
            HttpMethod.Put,
            $"/api/admin/users/{existingUserId}/roles",
            () => JsonSerializer.Serialize(new { roles = Array.Empty<string>() }),
            HttpStatusCode.OK),
        new("delete role", HttpMethod.Delete, $"/api/admin/roles/{existingRoleId}", () => string.Empty, HttpStatusCode.NoContent)
    ];

    [Fact]
    public async Task EveryAdministrativeOperationRequiresAnAdministratorToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        var administratorToken = await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken);
        using var administrator = AdminTestSupport.WithBearer(factory, administratorToken);

        var operatorUser = await AdminTestSupport.CreateUserAsync(
            administrator, "plain.user@example.test", "Passw0rd!", cancellationToken: cancellationToken);
        var plainToken = await AdminTestSupport.LoginAsync(anonymous, "plain.user@example.test", "Passw0rd!", cancellationToken);
        using var plainUser = AdminTestSupport.WithBearer(factory, plainToken);
        var probeRole = await AdminTestSupport.CreateRoleAsync(administrator, "AccessProbe", cancellationToken);

        foreach (var operation in Operations(operatorUser.Id, probeRole.Id))
        {
            using var noToken = await SendAsync(anonymous, operation, cancellationToken);
            Assert.True(noToken.StatusCode == HttpStatusCode.Unauthorized, $"{operation.Name}: no token -> {(int)noToken.StatusCode}");
            AssertChallenge(noToken, operation.Name);

            using var forbidden = await SendAsync(plainUser, operation, cancellationToken);
            Assert.True(forbidden.StatusCode == HttpStatusCode.Forbidden, $"{operation.Name}: non-administrator -> {(int)forbidden.StatusCode}");
            Assert.Equal(ProblemJson, forbidden.Content.Headers.ContentType?.MediaType);

            using var allowed = await SendAsync(administrator, operation, cancellationToken);
            Assert.True(
                allowed.StatusCode == operation.AdministratorStatus,
                $"{operation.Name}: administrator -> {(int)allowed.StatusCode}");
        }
    }

    [Fact]
    public async Task InvalidTokensAreRejectedWith401AndNoErrorDetail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var signer = new TestTokenMinter(File.ReadAllText(factory.Resources.PrivateKeyPath));
        using var unrelated = new TestTokenMinter();
        var now = DateTimeOffset.UtcNow;

        var cases = new (string Name, string Token)[]
        {
            ("forged signature", unrelated.Mint(new TestTokenRequest())),
            ("expired beyond tolerance", signer.Mint(Expired(now, TimeSpan.FromMinutes(10)))),
            ("expired just beyond the 30 second tolerance", signer.Mint(Expired(now, TimeSpan.FromSeconds(90)))),
            ("wrong issuer", signer.Mint(new TestTokenRequest { Issuer = "https://other-issuer.test" })),
            ("wrong audience", signer.Mint(new TestTokenRequest { Audience = "other-audience" })),
            ("non-RS256 algorithm", signer.Mint(new TestTokenRequest { Algorithm = TestTokenAlgorithm.Rs512 })),
            ("unsigned", signer.Mint(new TestTokenRequest { Algorithm = TestTokenAlgorithm.None })),
            ("malformed", "not-a-jwt")
        };

        foreach (var (name, token) in cases)
        {
            using var response = await GetUsersAsync(anonymous, token, cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name} -> {(int)response.StatusCode}");
            AssertChallenge(response, name);
        }

        // Expired, but within the 30 second tolerance: still accepted, exactly as the consumers do.
        using var withinTolerance = await GetUsersAsync(
            anonymous, signer.Mint(Expired(now, TimeSpan.FromSeconds(10))), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, withinTolerance.StatusCode);
    }

    [Fact]
    public async Task Phase1LoginContractIsUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();

        using var success = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = "admin@local.invalid", password = "admin" }, cancellationToken);
        using var document = JsonDocument.Parse(await success.Content.ReadAsStringAsync(cancellationToken));

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(
            ["accessToken", "expiresAtUtc"],
            document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());

        using var failure = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = "admin@local.invalid", password = "wrong" }, cancellationToken);
        var failureBody = await failure.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        Assert.Contains("Invalid credentials.", failureBody, StringComparison.Ordinal);
        Assert.Equal(DatabaseInitializer.AdministratorUserId, DecodeSubject(
            document.RootElement.GetProperty("accessToken").GetString()!));
    }

    private static TestTokenRequest Expired(DateTimeOffset now, TimeSpan expiredFor) => new()
    {
        IssuedAt = now - expiredFor - TimeSpan.FromMinutes(15),
        ExpiresAt = now - expiredFor
    };

    private static void AssertChallenge(HttpResponseMessage response, string name)
    {
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);

        Assert.True(
            challenge.Scheme == "Bearer" && string.IsNullOrEmpty(challenge.Parameter),
            $"{name}: challenge leaks detail");
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Operation operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(operation.Method, operation.Path);
        var body = operation.Body();
        if (body.Length > 0)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> GetUsersAsync(HttpClient client, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, cancellationToken);
    }

    private static string DecodeSubject(string token)
    {
        var payload = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Decode(token.Split('.')[1]);
        using var document = JsonDocument.Parse(payload);

        return document.RootElement.GetProperty("sub").GetString()!;
    }
}
