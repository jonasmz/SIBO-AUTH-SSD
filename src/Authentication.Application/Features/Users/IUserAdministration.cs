using Authentication.Application.Features.Administration;

namespace Authentication.Application.Features.Users;

public interface IUserAdministration
{
    Task<IReadOnlyList<UserView>> ListAsync(CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> FindAsync(string id, CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> UpdateEmailAsync(string id, string email, CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken);
}
