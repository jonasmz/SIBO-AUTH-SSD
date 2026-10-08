using Authentication.Api.Documentation;
using Authentication.Api.Features.Administration;
using Authentication.Application.Features.Roles;

namespace Authentication.Api.Features.Roles;

public static class RoleAdministrationEndpoints
{
    public static RouteGroupBuilder MapRoleAdministrationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/roles", ListAsync)
            .WithSummary("List roles")
            .Produces<IReadOnlyList<RoleView>>(StatusCodes.Status200OK);
        group.MapPost("/roles", CreateAsync)
            .WithSummary("Create a role")
            .DocumentsJsonRequest<RoleNameRequest>()
            .Produces<RoleView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPatch("/roles/{id}", RenameAsync)
            .WithSummary("Rename a role")
            .DocumentsJsonRequest<RoleNameRequest>()
            .Produces<RoleView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/roles/{id}", DeleteAsync)
            .WithSummary("Delete a role")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }

    private static Task<IResult> ListAsync(IRoleAdministration roles, CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () => Results.Ok(await roles.ListAsync(cancellationToken)));

    private static Task<IResult> CreateAsync(
        HttpRequest httpRequest,
        IRoleAdministration roles,
        CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () =>
        {
            var request = await AdministrationRequests.ReadAsync<RoleNameRequest>(httpRequest, cancellationToken);
            if (request is null || !request.IsValid)
            {
                return AdministrationResults.InvalidRequest();
            }

            return (await roles.CreateAsync(request.TrimmedName, cancellationToken))
                .ToCreated(role => $"/api/admin/roles/{role.Id}");
        });

    private static Task<IResult> RenameAsync(
        string id,
        HttpRequest httpRequest,
        IRoleAdministration roles,
        CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () =>
        {
            var request = await AdministrationRequests.ReadAsync<RoleNameRequest>(httpRequest, cancellationToken);
            if (request is null || !request.IsValid)
            {
                return AdministrationResults.InvalidRequest();
            }

            return (await roles.RenameAsync(id, request.TrimmedName, cancellationToken)).ToOk();
        });

    private static Task<IResult> DeleteAsync(string id, IRoleAdministration roles, CancellationToken cancellationToken) =>
        AdministrationResults.RunAsync(async () => (await roles.DeleteAsync(id, cancellationToken)).ToNoContent());
}
