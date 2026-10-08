using Authentication.Application.Features.Administration;

namespace Authentication.Application.Features.Roles;

public interface IRoleAdministration
{
    Task<IReadOnlyList<RoleView>> ListAsync(CancellationToken cancellationToken);

    Task<AdministrationResult<RoleView>> CreateAsync(string name, CancellationToken cancellationToken);

    Task<AdministrationResult<RoleView>> RenameAsync(string id, string name, CancellationToken cancellationToken);

    Task<AdministrationResult<RoleView>> DeleteAsync(string id, CancellationToken cancellationToken);
}
