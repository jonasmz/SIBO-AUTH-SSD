using Authentication.Application.Features.Administration;

namespace Authentication.Application.Features.Users;

public interface IUserAdministration
{
    Task<IReadOnlyList<UserView>> ListAsync(CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> FindAsync(string id, CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> UpdateEmailAsync(string id, string email, CancellationToken cancellationToken);

    Task<AdministrationResult<UserView>> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken);

    /// <summary>Replaces the user's complete role set with exactly the supplied existing roles.</summary>
    Task<AdministrationResult<UserView>> ReplaceRolesAsync(
        string id,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken);

    /// <summary>Revokes every active renewable-session family of an existing user; the value is the number revoked.</summary>
    Task<AdministrationResult<int>> RevokeSessionsAsync(string id, CancellationToken cancellationToken);
}
