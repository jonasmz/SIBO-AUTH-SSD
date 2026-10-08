using Authentication.Api.Features.Administration;
using Authentication.Application.Features.Roles;

namespace Authentication.Api.Features.Roles;

public static class RoleAdministrationEndpoints
{
    public static RouteGroupBuilder MapRoleAdministrationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/roles", ListAsync);
        group.MapPost("/roles", CreateAsync);
        group.MapPatch("/roles/{id}", RenameAsync);
        group.MapDelete("/roles/{id}", DeleteAsync);

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
