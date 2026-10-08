using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Infrastructure.Sessions;

internal static class SessionFamilyRevocation
{
    /// <summary>
    /// Revokes the user's active renewable-session families, except <paramref name="keepFamilyId"/>, and saves.
    /// It runs inside the caller's transaction and returns how many families changed.
    /// </summary>
    public static async Task<int> RevokeActiveAsync(
        AuthenticationDbContext context,
        string userId,
        DateTimeOffset now,
        SessionRevocationReason reason,
        string? keepFamilyId,
        CancellationToken cancellationToken)
    {
        var families = await context.RenewableSessionFamilies
            .Where(family => family.UserId == userId && family.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        var toRevoke = families.Where(family => family.IsActive(now) && family.Id != keepFamilyId).ToList();
        foreach (var family in toRevoke)
        {
            family.Revoke(now, reason);
        }

        await context.SaveChangesAsync(cancellationToken);

        return toRevoke.Count;
    }
}
