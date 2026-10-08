using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class RoleAdministrationTests
{
    private static readonly string[] OperatorOnly = ["Operator"];
    private static readonly string[] AuditorOnly = ["Auditor"];
    private static readonly string[] AuditorThenOperator = ["Auditor", "Operator"];
    private static readonly string[] OperatorTwice = ["Operator", "operator", "OPERATOR"];
    private static readonly string[] OperatorAndMissing = ["Operator", "NoSuchRole"];
    private static readonly string[] AuditorAnyCase = ["auditor"];
    private static readonly string[] OperatorsOnly = ["Operators"];

    private sealed record CallerBody(string Service, string Subject, string[] Roles);

    [Fact]
    public async Task RolesAreCreatedRenamedAndDeletedUnderTheIntegrityRules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));

        using var created = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Operator" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var operatorRole = (await created.Content.ReadFromJsonAsync<AdminRoleBody>(cancellationToken))!;
        Assert.Equal("Operator", operatorRole.Name);
        Assert.Equal($"/api/admin/roles/{operatorRole.Id}", created.Headers.Location?.OriginalString);

        var names = (await ListAsync(admin, cancellationToken)).Select(role => role.Name).ToArray();
        Assert.Equal(["Administrator", "Operator"], names);

        // Names are unique after normalization, and the request shape is checked.
        using var duplicate = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "operator" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        foreach (var body in new[] { """{"name":""}""", """{"name":"   "}""", $$"""{"name":"{{new string('x', 257)}}"}""", """{"name":"X","extra":1}""", "{}" })
        {
            using var invalid = await SendAsync(admin, HttpMethod.Post, "/api/admin/roles", body, cancellationToken);
            Assert.True(invalid.StatusCode == HttpStatusCode.BadRequest, $"{body.Length} -> {(int)invalid.StatusCode}");
        }

        // Rename: unused name succeeds, a colliding name is refused, unknown ids are 404.
        var support = await AdminTestSupport.CreateRoleAsync(admin, "Support", cancellationToken);
        using var renamed = await SendAsync(admin, HttpMethod.Patch, $"/api/admin/roles/{support.Id}", """{"name":"Helpdesk"}""", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Helpdesk", (await renamed.Content.ReadFromJsonAsync<AdminRoleBody>(cancellationToken))!.Name);
        using var collision = await SendAsync(admin, HttpMethod.Patch, $"/api/admin/roles/{support.Id}", """{"name":"OPERATOR"}""", cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        using var blankRename = await SendAsync(admin, HttpMethod.Patch, $"/api/admin/roles/{support.Id}", """{"name":""}""", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, blankRename.StatusCode);
        using var missingRename = await SendAsync(admin, HttpMethod.Patch, "/api/admin/roles/no-such-role", """{"name":"Z"}""", cancellationToken);
        using var missingDelete = await admin.DeleteAsync("/api/admin/roles/no-such-role", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingRename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingDelete.StatusCode);

        // Delete: an unassigned role goes away, an assigned role is refused and nothing changes.
        using var deleted = await admin.DeleteAsync($"/api/admin/roles/{support.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        _ = await AdminTestSupport.CreateUserAsync(
            admin, "holder@example.test", "Passw0rd!", OperatorOnly, cancellationToken: cancellationToken);
        using var refused = await admin.DeleteAsync($"/api/admin/roles/{operatorRole.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(["Administrator", "Operator"], (await ListAsync(admin, cancellationToken)).Select(role => role.Name).ToArray());
    }

    [Fact]
    public async Task SettingRolesReplacesTheSetAndNewTokensFollowItWhileOldTokensKeepTheirClaims()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        var operatorRole = await AdminTestSupport.CreateRoleAsync(admin, "Operator", cancellationToken);
        _ = await AdminTestSupport.CreateRoleAsync(admin, "Auditor", cancellationToken);
        var first = await AdminTestSupport.CreateUserAsync(
            admin, "first@example.test", "Passw0rd!", OperatorOnly, cancellationToken: cancellationToken);
        var second = await AdminTestSupport.CreateUserAsync(
            admin, "second@example.test", "Passw0rd!", OperatorOnly, cancellationToken: cancellationToken);

        // The supplied list replaces the whole set, only for that user.
        var replaced = await PutRolesAsync(admin, first.Id, """{"roles":["auditor"]}""", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(AuditorOnly, (await replaced.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken))!.Roles);
        Assert.Equal(OperatorOnly, (await GetUserAsync(admin, second.Id, cancellationToken)).Roles);
        Assert.Equal(AuditorOnly, AdminTestSupport.RolesOf(
            await AdminTestSupport.LoginAsync(anonymous, "first@example.test", "Passw0rd!", cancellationToken)));

        // Duplicates collapse after normalization; an empty list leaves no roles.
        using var many = await PutRolesAsync(admin, first.Id, JsonSerializer.Serialize(new { roles = OperatorTwice.Append("Auditor") }), cancellationToken);
        Assert.Equal(AuditorThenOperator, (await many.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken))!.Roles);
        using var none = await PutRolesAsync(admin, first.Id, """{"roles":[]}""", cancellationToken);
        Assert.Empty((await none.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken))!.Roles);
        Assert.Empty(AdminTestSupport.RolesOf(
            await AdminTestSupport.LoginAsync(anonymous, "first@example.test", "Passw0rd!", cancellationToken)));

        // A nonexistent role rejects the whole request; nothing is applied.
        _ = await PutRolesAsync(admin, first.Id, """{"roles":["Operator"]}""", cancellationToken);
        using var partial = await PutRolesAsync(admin, first.Id, JsonSerializer.Serialize(new { roles = OperatorAndMissing }), cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        Assert.Equal(OperatorOnly, (await GetUserAsync(admin, first.Id, cancellationToken)).Roles);
        using var missingUser = await PutRolesAsync(admin, "no-such-user", """{"roles":[]}""", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingUser.StatusCode);

        // The request shape is checked.
        foreach (var body in new[] { "{}", """{"roles":[""]}""", """{"roles":["Operator"],"extra":1}""", """{"roles":"Operator"}""" })
        {
            using var invalid = await PutRolesAsync(admin, first.Id, body, cancellationToken);
            Assert.True(invalid.StatusCode == HttpStatusCode.BadRequest, $"{body} -> {(int)invalid.StatusCode}");
        }

        // Renaming keeps assignments, and new tokens carry the new name.
        using var rename = await SendAsync(admin, HttpMethod.Patch, $"/api/admin/roles/{operatorRole.Id}", """{"name":"Operators"}""", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(OperatorsOnly, (await GetUserAsync(admin, second.Id, cancellationToken)).Roles);
        Assert.Equal(OperatorsOnly, AdminTestSupport.RolesOf(
            await AdminTestSupport.LoginAsync(anonymous, "second@example.test", "Passw0rd!", cancellationToken)));

        // A token issued before a role change keeps the claims it was issued with; a new one follows the change.
        var oldToken = await AdminTestSupport.LoginAsync(anonymous, "second@example.test", "Passw0rd!", cancellationToken);
        var changed = await PutRolesAsync(admin, second.Id, JsonSerializer.Serialize(new { roles = AuditorAnyCase }), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var newToken = await AdminTestSupport.LoginAsync(anonymous, "second@example.test", "Passw0rd!", cancellationToken);

        using var apiA = new ReferenceConsumerFactory("api-a", factory.Resources.PublicKeyPem);
        using var consumer = apiA.CreateClient();
        Assert.Equal(OperatorsOnly, (await CallerAsync(consumer, oldToken, cancellationToken)).Roles);
        Assert.Equal(AuditorOnly, (await CallerAsync(consumer, newToken, cancellationToken)).Roles);
    }

    private static async Task<IReadOnlyList<AdminRoleBody>> ListAsync(HttpClient admin, CancellationToken cancellationToken) =>
        (await admin.GetFromJsonAsync<AdminRoleBody[]>("/api/admin/roles", cancellationToken))!;

    private static async Task<AdminUserBody> GetUserAsync(HttpClient admin, string id, CancellationToken cancellationToken) =>
        (await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{id}", cancellationToken))!;

    private static Task<HttpResponseMessage> PutRolesAsync(HttpClient admin, string id, string json, CancellationToken cancellationToken) =>
        SendAsync(admin, HttpMethod.Put, $"/api/admin/users/{id}/roles", json, cancellationToken);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<CallerBody> CallerAsync(HttpClient consumer, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/caller");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await consumer.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CallerBody>(cancellationToken))!;
    }
}
