using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Authentication.IntegrationTests.Infrastructure;

public sealed record LoginTokenBody(string AccessToken, DateTime ExpiresAtUtc);

public sealed record AdminUserBody(
    string Id,
    string Email,
    bool Enabled,
    bool IsLockedOut,
    DateTime? LockoutEndUtc,
    string[] Roles);

/// <summary>Shared helpers for the Phase 3 scenario classes; they only call the public HTTP surface.</summary>
public static class AdminTestSupport
{
    public const string AdministratorEmail = "admin@local.invalid";
    public const string AdministratorPassword = "admin";

    /// <summary>Signs in through <c>/api/auth/login</c> and returns the access token.</summary>
    public static async Task<string> LoginAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginTokenBody>(cancellationToken);

        return body!.AccessToken;
    }

    /// <summary>Returns the access token of the built-in administrator.</summary>
    public static Task<string> AdministratorTokenAsync(HttpClient client, CancellationToken cancellationToken) =>
        LoginAsync(client, AdministratorEmail, AdministratorPassword, cancellationToken);

    /// <summary>Creates a new client for the same host that sends the given bearer token.</summary>
    public static HttpClient WithBearer(AuthenticationApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>Creates a user through the administrative endpoint and returns the created view.</summary>
    public static async Task<AdminUserBody> CreateUserAsync(
        HttpClient administratorClient,
        string email,
        string password,
        IReadOnlyList<string>? roles = null,
        bool? enabled = null,
        CancellationToken cancellationToken = default)
    {
        var request = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["password"] = password,
            ["roles"] = roles ?? []
        };
        if (enabled is not null)
        {
            request["enabled"] = enabled;
        }

        using var response = await administratorClient.PostAsJsonAsync("/api/admin/users", request, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken))!;
    }
}
