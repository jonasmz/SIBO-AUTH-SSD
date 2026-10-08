using Authentication.Api.Features.Administration;
using Authentication.Application.Features.Users;

namespace Authentication.Api.Features.Users;

public static class UserAdministrationEndpoints
{
    public static RouteGroupBuilder MapUserAdministrationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/users", ListAsync);
        group.MapGet("/users/{id}", GetAsync);
        group.MapPost("/users", CreateAsync);
        group.MapPatch("/users/{id}", UpdateAsync);
        group.MapPut("/users/{id}/roles", ReplaceRolesAsync);
        group.MapPost("/users/{id}/revoke-sessions", RevokeSessionsAsync);
        group.MapPost("/users/{id}/enable", (string id, IUserAdministration users, CancellationToken cancellationToken) =>
            SetEnabledAsync(id, true, users, cancellationToken));
        group.MapPost("/users/{id}/disable", (string id, IUserAdministration users, CancellationToken cancellationToken) =>
            SetEnabledAsync(id, false, users, cancellationToken));

        return group;
    }

    private static Task<IResult> SetEnabledAsync(
        string id,
        bool enabled,
        IUserAdministration users,
        CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () => (await users.SetEnabledAsync(id, enabled, cancellationToken)).ToOk());

    private static Task<IResult> RevokeSessionsAsync(string id, IUserAdministration users, CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () => (await users.RevokeSessionsAsync(id, cancellationToken)).ToNoContent());

    private static Task<IResult> ListAsync(IUserAdministration users, CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () => Results.Ok(await users.ListAsync(cancellationToken)));

    private static Task<IResult> GetAsync(string id, IUserAdministration users, CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () => (await users.FindAsync(id, cancellationToken)).ToOk());

    private static Task<IResult> CreateAsync(
        HttpRequest httpRequest,
        IUserAdministration users,
        CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () =>
        {
            var request = await AdministrationRequests.ReadAsync<CreateUserRequest>(httpRequest, cancellationToken);
            if (request is null || !request.IsValid)
            {
                return AdministrationResults.InvalidRequest();
            }

            var command = new CreateUserCommand(
                request.Email!,
                request.Password!,
                request.Enabled ?? true,
                request.Roles ?? []);

            return (await users.CreateAsync(command, cancellationToken))
                .ToCreated(user => $"/api/admin/users/{user.Id}");
        });

    private static Task<IResult> UpdateAsync(
        string id,
        HttpRequest httpRequest,
        IUserAdministration users,
        CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () =>
        {
            var request = await AdministrationRequests.ReadAsync<UpdateUserRequest>(httpRequest, cancellationToken);
            if (request is null || !request.IsValid)
            {
                return AdministrationResults.InvalidRequest();
            }

            return (await users.UpdateEmailAsync(id, request.Email!, cancellationToken)).ToOk();
        });

    private static Task<IResult> ReplaceRolesAsync(
        string id,
        HttpRequest httpRequest,
        IUserAdministration users,
        CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () =>
        {
            var request = await AdministrationRequests.ReadAsync<ReplaceUserRolesRequest>(httpRequest, cancellationToken);
            if (request is null || !request.IsValid)
            {
                return AdministrationResults.InvalidRequest();
            }

            return (await users.ReplaceRolesAsync(id, request.Roles!, cancellationToken)).ToOk();
        });
}
