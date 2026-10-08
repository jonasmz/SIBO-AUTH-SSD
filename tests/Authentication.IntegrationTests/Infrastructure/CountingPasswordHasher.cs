using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Authentication.IntegrationTests.Infrastructure;

/// <summary>
/// Test-host decorator over Identity's hasher that counts verifications, so "equivalent password-verification
/// work" is measured by calls and never by duration (SRS NFR-SEC-ENUM-004, NFR-002). Hashing and the verdicts
/// are the real Identity behavior.
/// </summary>
public sealed class CountingPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private readonly PasswordHasher<ApplicationUser> _inner = new();
    private int _verifications;

    public int Verifications => Volatile.Read(ref _verifications);

    public string HashPassword(ApplicationUser user, string password) => _inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        _ = Interlocked.Increment(ref _verifications);

        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
