using Authentication.Application.Features.Login;
using Microsoft.AspNetCore.Identity;

namespace Authentication.Infrastructure.Identity;

public sealed class IdentityCredentialValidator(
    UserManager<ApplicationUser> userManager,
    IPasswordHasher<ApplicationUser> passwordHasher) : IIdentityCredentialValidator
{
    private static readonly ApplicationUser DummyUser = new() { Id = "dummy", UserName = "dummy" };

    public async Task<AuthenticatedIdentity?> ValidateAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Spend hasher-equivalent work so unknown emails are not distinguishable by timing.
            passwordHasher.VerifyHashedPassword(DummyUser, DummyHash(), password);
            return null;
        }

        if (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            if (userManager.SupportsUserLockout)
            {
                await userManager.AccessFailedAsync(user);
            }

            return null;
        }

        if (userManager.SupportsUserLockout)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        var roles = await userManager.GetRolesAsync(user);

        return new AuthenticatedIdentity(user.Id, user.Email ?? email, [.. roles]);
    }

    private string DummyHash() => _dummyHash ??= passwordHasher.HashPassword(DummyUser, Guid.NewGuid().ToString("N"));

    private static string? _dummyHash;
}
