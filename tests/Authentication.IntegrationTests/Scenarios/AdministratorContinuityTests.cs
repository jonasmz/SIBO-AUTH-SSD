using System.Net;
using System.Net.Http.Json;
using System.Text;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

public sealed class AdministratorContinuityTests
{
    private const string AdministratorId = DatabaseInitializer.AdministratorUserId;
    private const string AdministratorRoleId = DatabaseInitializer.AdministratorRoleId;
    private static readonly string[] AdministratorOnly = ["Administrator"];
    private static readonly string[] NoRoles = [];

    [Fact]
    public async Task TheSoleEnabledAdministratorCannotBeDisabledDemotedOrLoseTheAdministratorRole()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        _ = await AdminTestSupport.CreateRoleAsync(admin, "Operator", cancellationToken);

        await AssertSoleAdministratorProtectedAsync(admin, cancellationToken);

        // A second administrator that is disabled does not count: the protections still hold.
        _ = await AdminTestSupport.CreateUserAsync(
            admin, "dormant@example.test", "Passw0rd!", AdministratorOnly, enabled: false, cancellationToken: cancellationToken);
        await AssertSoleAdministratorProtectedAsync(admin, cancellationToken);
    }

    [Fact]
    public async Task RefusedDisableOfTheLastAdministratorLeavesItsRenewableFamilyUsable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var browser = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var credential = await AdministrativeSessionRevocationTests.LoginCookieAsync(
            browser, AdminTestSupport.AdministratorEmail, AdminTestSupport.AdministratorPassword, cancellationToken);
        using var admin = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(browser, cancellationToken));

        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(admin, $"/api/admin/users/{AdministratorId}/disable", cancellationToken));

        var user = await GetUserAsync(admin, AdministratorId, cancellationToken);
        Assert.True(user.Enabled);
        using var refresh = await AdministrativeSessionRevocationTests.RefreshAsync(browser, credential, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task WithSeveralEnabledAdministratorsOnlyTheLastOneIsProtectedWhoeverPerformsTheOperation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        using var first = AdminTestSupport.WithBearer(
            factory, await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken));
        var second = await AdminTestSupport.CreateUserAsync(
            first, "second.admin@example.test", "Passw0rd!", AdministratorOnly, cancellationToken: cancellationToken);

        // The second administrator can be demoted and restored while the first remains.
        Assert.Equal(HttpStatusCode.OK, await SetRolesStatusAsync(first, second.Id, NoRoles, cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(first, $"/api/admin/users/{AdministratorId}/disable", cancellationToken));
        Assert.Equal(HttpStatusCode.OK, await SetRolesStatusAsync(first, second.Id, AdministratorOnly, cancellationToken));

        // A locked-out administrator is still an enabled administrator.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await anonymous.PostAsJsonAsync(
                "/api/auth/login", new { email = "second.admin@example.test", password = "Wrong-Password1" }, cancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        Assert.True((await GetUserAsync(first, second.Id, cancellationToken)).IsLockedOut);

        // The first administrator is disabled by itself; its still-valid token then acts on the last one.
        Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(first, $"/api/admin/users/{AdministratorId}/disable", cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(first, $"/api/admin/users/{second.Id}/disable", cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await SetRolesStatusAsync(first, second.Id, NoRoles, cancellationToken));

        var survivor = await GetUserAsync(first, second.Id, cancellationToken);
        Assert.True(survivor.Enabled);
        Assert.Equal(AdministratorOnly, survivor.Roles);
    }

    [Fact]
    public async Task SimultaneousDisablesOfTwoAdministratorsLeaveExactlyOneEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new AuthenticationApiFactory();
        using var anonymous = factory.CreateClient();
        var firstToken = await AdminTestSupport.AdministratorTokenAsync(anonymous, cancellationToken);
        using var first = AdminTestSupport.WithBearer(factory, firstToken);
        var second = await AdminTestSupport.CreateUserAsync(
            first, "second.admin@example.test", "Passw0rd!", AdministratorOnly, cancellationToken: cancellationToken);
        using var secondClient = AdminTestSupport.WithBearer(
            factory,
            await AdminTestSupport.LoginAsync(anonymous, "second.admin@example.test", "Passw0rd!", cancellationToken));

        for (var round = 0; round < 6; round++)
        {
            // Both administrators enabled again, then each one disables the other at the same instant.
            Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(first, $"/api/admin/users/{AdministratorId}/enable", cancellationToken));
            Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(first, $"/api/admin/users/{second.Id}/enable", cancellationToken));

            var gate = new TaskCompletionSource();
            var disableFirst = Task.Run(async () =>
            {
                await gate.Task;
                return await PostStatusAsync(secondClient, $"/api/admin/users/{AdministratorId}/disable", cancellationToken);
            }, cancellationToken);
            var disableSecond = Task.Run(async () =>
            {
                await gate.Task;
                return await PostStatusAsync(first, $"/api/admin/users/{second.Id}/disable", cancellationToken);
            }, cancellationToken);
            gate.SetResult();

            var statuses = (await Task.WhenAll(disableFirst, disableSecond)).Order().ToArray();
            Assert.True(
                statuses.SequenceEqual([HttpStatusCode.OK, HttpStatusCode.Conflict]),
                $"round {round}: {string.Join(",", statuses.Select(status => (int)status))}");

            var users = (await first.GetFromJsonAsync<AdminUserBody[]>("/api/admin/users", cancellationToken))!;
            Assert.Single(users, user => user.Enabled && user.Roles.Contains("Administrator"));
        }
    }

    private static async Task AssertSoleAdministratorProtectedAsync(HttpClient admin, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Conflict, await PostStatusAsync(admin, $"/api/admin/users/{AdministratorId}/disable", cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await SetRolesStatusAsync(admin, AdministratorId, NoRoles, cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await SetRolesStatusAsync(admin, AdministratorId, ["Operator"], cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await SendStatusAsync(
            admin, HttpMethod.Patch, $"/api/admin/roles/{AdministratorRoleId}", """{"name":"Admins"}""", cancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, await SendStatusAsync(
            admin, HttpMethod.Delete, $"/api/admin/roles/{AdministratorRoleId}", null, cancellationToken));

        var user = await GetUserAsync(admin, AdministratorId, cancellationToken);
        Assert.True(user.Enabled);
        Assert.Equal(AdministratorOnly, user.Roles);
        var roles = (await admin.GetFromJsonAsync<AdminRoleBody[]>("/api/admin/roles", cancellationToken))!;
        Assert.Contains(roles, role => role.Id == AdministratorRoleId && role.Name == "Administrator");
    }

    private static async Task<AdminUserBody> GetUserAsync(HttpClient admin, string id, CancellationToken cancellationToken) =>
        (await admin.GetFromJsonAsync<AdminUserBody>($"/api/admin/users/{id}", cancellationToken))!;

    private static Task<HttpStatusCode> SetRolesStatusAsync(
        HttpClient admin,
        string id,
        string[] roles,
        CancellationToken cancellationToken) =>
        SendStatusAsync(
            admin,
            HttpMethod.Put,
            $"/api/admin/users/{id}/roles",
            System.Text.Json.JsonSerializer.Serialize(new { roles }),
            cancellationToken);

    private static Task<HttpStatusCode> PostStatusAsync(HttpClient client, string path, CancellationToken cancellationToken) =>
        SendStatusAsync(client, HttpMethod.Post, path, null, cancellationToken);

    private static async Task<HttpStatusCode> SendStatusAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? json,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);

        return response.StatusCode;
    }
}
