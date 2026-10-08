using System.Net;
using System.Text.Json;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class ApiDocumentationTests
{
    // specs/008-phase-8-operations-final-integration/contracts/openapi-coverage.md
    private static readonly (string Method, string Path, bool Bearer, string? Body, int[] Statuses)[] Contract =
    [
        ("post", "/api/auth/login", false, "LoginRequest", [200, 400, 401, 429, 503]),
        ("post", "/api/auth/refresh", false, null, [200, 401, 403, 429, 503]),
        ("post", "/api/auth/logout", false, null, [204, 403, 503]),
        ("post", "/api/auth/change-password", true, "ChangePasswordRequest", [204, 400, 401, 503]),
        ("post", "/api/auth/forgot-password", false, "ForgotPasswordRequest", [204, 400, 429, 503]),
        ("post", "/api/auth/reset-password", false, "ResetPasswordRequest", [204, 400, 401, 429, 503]),
        ("get", "/api/admin/users", true, null, [200, 401, 403, 503]),
        ("get", "/api/admin/users/{id}", true, null, [200, 401, 403, 404, 503]),
        ("post", "/api/admin/users", true, "CreateUserRequest", [201, 400, 401, 403, 409, 503]),
        ("patch", "/api/admin/users/{id}", true, "UpdateUserRequest", [200, 400, 401, 403, 404, 409, 503]),
        ("put", "/api/admin/users/{id}/roles", true, "ReplaceUserRolesRequest", [200, 400, 401, 403, 404, 409, 503]),
        ("post", "/api/admin/users/{id}/enable", true, null, [200, 401, 403, 404, 409, 503]),
        ("post", "/api/admin/users/{id}/disable", true, null, [200, 401, 403, 404, 409, 503]),
        ("post", "/api/admin/users/{id}/revoke-sessions", true, null, [204, 401, 403, 404, 503]),
        ("get", "/api/admin/roles", true, null, [200, 401, 403, 503]),
        ("post", "/api/admin/roles", true, "RoleNameRequest", [201, 400, 401, 403, 409, 503]),
        ("patch", "/api/admin/roles/{id}", true, "RoleNameRequest", [200, 400, 401, 403, 404, 409, 503]),
        ("delete", "/api/admin/roles/{id}", true, null, [204, 401, 403, 404, 409, 503]),
        ("get", "/health/live", false, null, [200]),
        ("get", "/health/ready", false, null, [200, 503]),
    ];

    [Fact]
    public async Task DevelopmentPublishesTheCompleteContractAndAReadOnlyViewer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory(environment: "Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;

        Assert.StartsWith("3.1", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);

        var scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());

        var operations = root.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => (Method: operation.Name, Path: path.Name, Operation: operation.Value)))
            .ToList();
        Assert.Equal(Contract.Length, operations.Count);

        foreach (var (method, path, bearer, body, statuses) in Contract)
        {
            var operation = Assert.Single(operations, candidate => candidate.Method == method && candidate.Path == path).Operation;
            var label = $"{method.ToUpperInvariant()} {path}";

            Assert.True(operation.TryGetProperty("summary", out _), $"{label} has no summary");
            Assert.Equal(statuses, operation.GetProperty("responses").EnumerateObject().Select(status => int.Parse(status.Name, System.Globalization.CultureInfo.InvariantCulture)).Order());

            var secured = operation.TryGetProperty("security", out var security) &&
                security.EnumerateArray().Any(requirement => requirement.TryGetProperty("Bearer", out _));
            Assert.True(bearer == secured, $"{label} Bearer requirement should be {bearer}");

            if (body is null)
            {
                Assert.False(operation.TryGetProperty("requestBody", out _), $"{label} should have no request body");
            }
            else
            {
                var reference = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
                    .GetProperty("schema").GetProperty("$ref").GetString();
                Assert.Equal($"#/components/schemas/{body}", reference);
            }

            // Every error is a problem-details response.
            foreach (var error in operation.GetProperty("responses").EnumerateObject().Where(status => status.Name[0] is '4' or '5'))
            {
                Assert.True(error.Value.GetProperty("content").TryGetProperty("application/problem+json", out _), $"{label} {error.Name} is not problem details");
            }
        }

        // Request schemas list only the fields a client sends, not the records' computed helpers.
        var schemas = root.GetProperty("components").GetProperty("schemas");
        foreach (var body in Contract.Select(entry => entry.Body).OfType<string>().Distinct())
        {
            var properties = schemas.GetProperty(body).GetProperty("properties");
            Assert.False(properties.TryGetProperty("isValid", out _), $"{body} exposes isValid");
            Assert.False(properties.TryGetProperty("trimmedName", out _), $"{body} exposes trimmedName");
        }

        // The viewer is served read-only: no test requests, no API client, no stored credentials, no telemetry.
        using var viewer = await client.GetAsync("/scalar/", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        var page = await viewer.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("\"hideTestRequestButton\":true", page, StringComparison.Ordinal);
        Assert.Contains("\"hideClientButton\":true", page, StringComparison.Ordinal);
        Assert.Contains("\"persistAuth\":false", page, StringComparison.Ordinal);
        Assert.Contains("\"telemetry\":false", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar")]
    [InlineData("/scalar/")]
    public async Task ProductionServesNeitherTheContractNorTheViewer(string path)
    {
        using var factory = new AuthenticationApiFactory(environment: "Production");
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
