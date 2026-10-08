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
            // Spend one hash verification, as every other refusal does. The result is ignored and
            // CheckPasswordAsync is not used, so no failure is counted and nothing is rehashed.
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash ?? DummyHash(), password);
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

        if (!user.IsEnabled)
        {
            // The password was verified like any other attempt; a disabled account gets the same
            // generic refusal and its failed-attempt counter is left untouched.
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
