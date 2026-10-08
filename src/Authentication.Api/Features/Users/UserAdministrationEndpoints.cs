using Authentication.Api.Documentation;
using Authentication.Api.Features.Administration;
using Authentication.Application.Features.Users;

namespace Authentication.Api.Features.Users;

public static class UserAdministrationEndpoints
{
    public static RouteGroupBuilder MapUserAdministrationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/users", ListAsync)
            .WithSummary("List users")
            .Produces<IReadOnlyList<UserView>>(StatusCodes.Status200OK);
        group.MapGet("/users/{id}", GetAsync)
            .WithSummary("Get one user")
            .Produces<UserView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/users", CreateAsync)
            .WithSummary("Create a user")
            .DocumentsJsonRequest<CreateUserRequest>()
            .Produces<UserView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPatch("/users/{id}", UpdateAsync)
            .WithSummary("Change a user's email")
            .DocumentsJsonRequest<UpdateUserRequest>()
            .Produces<UserView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/users/{id}/roles", ReplaceRolesAsync)
            .WithSummary("Replace a user's roles")
            .DocumentsJsonRequest<ReplaceUserRolesRequest>()
            .Produces<UserView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/users/{id}/revoke-sessions", RevokeSessionsAsync)
            .WithSummary("Revoke every renewable session of a user")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/users/{id}/enable", (string id, IUserAdministration users, CancellationToken cancellationToken) =>
                SetEnabledAsync(id, true, users, cancellationToken))
            .WithSummary("Enable a user")
            .Produces<UserView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/users/{id}/disable", (string id, IUserAdministration users, CancellationToken cancellationToken) =>
                SetEnabledAsync(id, false, users, cancellationToken))
            .WithSummary("Disable a user and revoke their renewable sessions")
            .Produces<UserView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
