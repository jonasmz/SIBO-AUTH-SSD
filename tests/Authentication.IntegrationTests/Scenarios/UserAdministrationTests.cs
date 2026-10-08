using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class UserAdministrationTests
{
    private static readonly string[] ViewMembers = ["email", "enabled", "id", "isLockedOut", "lockoutEndUtc", "roles"];
    private static readonly string[] AdministratorRoleOnly = ["Administrator"];
    private static readonly string[] AdministratorRoleAnyCase = ["administrator"];
    private static readonly string[] NonexistentRole = ["NoSuchRole"];

    [Fact]
    public async Task CreateListAndGetEnforceUniquenessPolicyRolesAndExposeOnlyPermittedFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));

        // Default enabled state, explicit enabled state, and an initial role.
        var first = await AdminTestSupport.CreateUserAsync(
            admin, "first@example.test", "Passw0rd!", cancellationToken: cancellationToken);
        var disabled = await AdminTestSupport.CreateUserAsync(
            admin, "second@example.test", "Passw0rd!", enabled: false, cancellationToken: cancellationToken);
        var withRole = await AdminTestSupport.CreateUserAsync(
            admin, "third@example.test", "Passw0rd!", AdministratorRoleAnyCase, cancellationToken: cancellationToken);
        Assert.True(first.Enabled);
        Assert.False(disabled.Enabled);
        Assert.Equal(AdministratorRoleOnly, withRole.Roles);

        using var created = await admin.PostAsJsonAsync(
            "/api/admin/users", new { email = "fourth@example.test", password = "Passw0rd!" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var fourth = await created.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken);
        Assert.Equal($"/api/admin/users/{fourth!.Id}", created.Headers.Location?.OriginalString);

        var countBefore = (await ListAsync(admin, cancellationToken)).Count;

        using var duplicate = await admin.PostAsJsonAsync(
            "/api/admin/users", new { email = "FIRST@Example.Test", password = "Passw0rd!" }, cancellationToken);
        using var weak = await admin.PostAsJsonAsync(
            "/api/admin/users", new { email = "weak@example.test", password = "ab" }, cancellationToken);
        using var missingRole = await admin.PostAsJsonAsync(
            "/api/admin/users",
            new { email = "norole@example.test", password = "Passw0rd!", roles = NonexistentRole },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingRole.StatusCode);
        Assert.Equal(countBefore, (await ListAsync(admin, cancellationToken)).Count);

        // List and get expose exactly the permitted members and nothing sensitive.
        var listText = await admin.GetStringAsync("/api/admin/users", cancellationToken);
        var getText = await admin.GetStringAsync($"/api/admin/users/{first.Id}", cancellationToken);
        foreach (var text in new[] { listText, getText })
        {
            Assert.DoesNotContain("passwordhash", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securitystamp", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("concurrencystamp", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Passw0rd!", text, StringComparison.Ordinal);
        }

        using var list = JsonDocument.Parse(listText);
        foreach (var user in list.RootElement.EnumerateArray())
        {
            Assert.Equal(ViewMembers, user.EnumerateObject().Select(member => member.Name).Order().ToArray());
        }

        using var single = JsonDocument.Parse(getText);
        Assert.Equal(ViewMembers, single.RootElement.EnumerateObject().Select(member => member.Name).Order().ToArray());
        Assert.False(single.RootElement.GetProperty("isLockedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, single.RootElement.GetProperty("lockoutEndUtc").ValueKind);
    }

    [Fact]
    public async Task UpdateChangesOnlyTheEmailAndRejectsEverythingElse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        var user = await AdminTestSupport.CreateUserAsync(
            admin, "update.me@example.test", "Passw0rd!", cancellationToken: cancellationToken);
        var other = await AdminTestSupport.CreateUserAsync(
            admin, "other@example.test", "Passw0rd!", cancellationToken: cancellationToken);

        // A valid email change moves the login identifier.
        using var changed = await PatchAsync(admin, user.Id, """{"email":"renamed@example.test"}""", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("renamed@example.test", (await changed.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken))!.Email);
        _ = await AdminTestSupport.LoginAsync(anonymous, "renamed@example.test", "Passw0rd!", cancellationToken);
        using var oldLogin = await anonymous.PostAsJsonAsync(
            "/api/auth/login", new { email = "update.me@example.test", password = "Passw0rd!" }, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        // The user's own current email is not a conflict with itself.
        using var same = await PatchAsync(admin, user.Id, """{"email":"RENAMED@example.test"}""", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);

        // Another user's email is a conflict.
        using var taken = await PatchAsync(admin, user.Id, """{"email":"other@example.test"}""", cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        // Any other attribute, an empty update, a malformed body, or a non-JSON body is invalid and changes nothing.
        foreach (var body in new[]
        {
            """{"email":"x@example.test","enabled":false}""",
            """{"email":"x@example.test","roles":["Administrator"]}""",
            """{"email":"x@example.test","password":"Another1!"}""",
            """{"email":"x@example.test","unknown":1}""",
            """{"enabled":false}""",
            "{}",
            """{"email":""",
            """{"email":"not-an-email"}"""
        })
        {
            using var response = await PatchAsync(admin, user.Id, body, cancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{body} -> {(int)response.StatusCode}");
        }

        using var plainPatch = await SendRawAsync(
            admin, HttpMethod.Patch, $"/api/admin/users/{user.Id}", """{"email":"x@example.test"}""", "text/plain", cancellationToken);
        using var plainPost = await SendRawAsync(
            admin, HttpMethod.Post, "/api/admin/users", """{"email":"y@example.test","password":"Passw0rd!"}""", "text/plain", cancellationToken);
        using var malformedPost = await SendRawAsync(
            admin, HttpMethod.Post, "/api/admin/users", """{"email":""", "application/json", cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, plainPatch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, plainPost.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformedPost.StatusCode);

        var after = await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{user.Id}", cancellationToken);
        Assert.Equal("renamed@example.test", after!.Email);
        Assert.Equal(2 + 1, (await ListAsync(admin, cancellationToken)).Count);
        Assert.Equal("other@example.test", (await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{other.Id}", cancellationToken))!.Email);

        // Unknown identifiers.
        using var missingGet = await admin.GetAsync("/api/admin/users/no-such-user", cancellationToken);
        using var missingPatch = await PatchAsync(admin, "no-such-user", """{"email":"z@example.test"}""", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingPatch.StatusCode);
    }

    [Fact]
    public async Task DisablingBlocksLoginAndEnablingRestoresItWithoutChangingTheAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        var user = await AdminTestSupport.CreateUserAsync(
            admin, "toggle@example.test", "Passw0rd!", AdministratorRoleOnly, cancellationToken: cancellationToken);
        _ = await AdminTestSupport.LoginAsync(anonymous, "toggle@example.test", "Passw0rd!", cancellationToken);

        var wrongPassword = await LoginFailureAsync(anonymous, "toggle@example.test", "Wrong-Password1", cancellationToken);

        using var disabled = await admin.PostAsync($"/api/admin/users/{user.Id}/disable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False((await disabled.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken))!.Enabled);

        // A disabled account with the right password gets exactly the generic credential failure.
        var refused = await LoginFailureAsync(anonymous, "toggle@example.test", "Passw0rd!", cancellationToken);
        Assert.Equal(wrongPassword, refused);

        // Disabling twice is idempotent.
        using var disabledAgain = await admin.PostAsync($"/api/admin/users/{user.Id}/disable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, disabledAgain.StatusCode);

        using var enabled = await admin.PostAsync($"/api/admin/users/{user.Id}/enable", null, cancellationToken);
        using var enabledAgain = await admin.PostAsync($"/api/admin/users/{user.Id}/enable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enabledAgain.StatusCode);

        // The same password works again, and email and roles survived the cycle.
        _ = await AdminTestSupport.LoginAsync(anonymous, "toggle@example.test", "Passw0rd!", cancellationToken);
        var after = await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{user.Id}", cancellationToken);
        Assert.True(after!.Enabled);
        Assert.Equal("toggle@example.test", after.Email);
        Assert.Equal(AdministratorRoleOnly, after.Roles);

        // A user created disabled cannot log in until enabled.
        var created = await AdminTestSupport.CreateUserAsync(
            admin, "born.disabled@example.test", "Passw0rd!", enabled: false, cancellationToken: cancellationToken);
        Assert.False(created.Enabled);
        Assert.Equal(
            await LoginFailureAsync(anonymous, "born.disabled@example.test", "Wrong-Password1", cancellationToken),
            await LoginFailureAsync(anonymous, "born.disabled@example.test", "Passw0rd!", cancellationToken));
        using var activated = await admin.PostAsync($"/api/admin/users/{created.Id}/enable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        _ = await AdminTestSupport.LoginAsync(anonymous, "born.disabled@example.test", "Passw0rd!", cancellationToken);

        using var missingEnable = await admin.PostAsync("/api/admin/users/no-such-user/enable", null, cancellationToken);
        using var missingDisable = await admin.PostAsync("/api/admin/users/no-such-user/disable", null, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingEnable.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingDisable.StatusCode);
    }

    [Fact]
    public async Task LockoutIsReportedIndependentlyOfTheEnabledState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        var user = await AdminTestSupport.CreateUserAsync(
            admin, "locked@example.test", "Passw0rd!", cancellationToken: cancellationToken);

        // Identity's default policy locks the account after five consecutive failures. Identity and
        // JwtBearer use the system clock, so the scenario runs on it and never advances time.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            _ = await LoginFailureAsync(anonymous, "locked@example.test", "Wrong-Password1", cancellationToken);
        }

        var locked = await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{user.Id}", cancellationToken);
        Assert.True(locked!.Enabled);
        Assert.True(locked.IsLockedOut);
        Assert.NotNull(locked.LockoutEndUtc);
        Assert.True(locked.LockoutEndUtc > DateTime.UtcNow);

        using var disabled = await admin.PostAsync($"/api/admin/users/{user.Id}/disable", null, cancellationToken);
        var both = await disabled.Content.ReadFromJsonAsync<AdminUserBody>(cancellationToken);
        Assert.False(both!.Enabled);
        Assert.True(both.IsLockedOut);
    }

    /// <summary>Returns the status and the normalized problem body of a refused login.</summary>
    private static async Task<string> LoginFailureAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return string.Join(
            "|",
            document.RootElement.EnumerateObject()
                .Where(member => member.Name != "traceId")
                .Select(member => $"{member.Name}={member.Value}"));
    }

    private static async Task<IReadOnlyList<AdminUserBody>> ListAsync(HttpClient admin, CancellationToken cancellationToken) =>
        (await admin.GetFromJsonAsync<AdminUserBody[]>("/api/admin/users", cancellationToken))!;

    private static Task<HttpResponseMessage> PatchAsync(HttpClient admin, string id, string json, CancellationToken cancellationToken) =>
        SendRawAsync(admin, HttpMethod.Patch, $"/api/admin/users/{id}", json, "application/json", cancellationToken);

    private static async Task<HttpResponseMessage> SendRawAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string body,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };

        return await client.SendAsync(request, cancellationToken);
    }
}
