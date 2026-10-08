using System.Data;
using Authentication.Application.Features.Administration;
using Authentication.Application.Features.Users;
using Authentication.Domain.Administration;
using Authentication.Domain.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Authentication.Infrastructure.Identity;

public sealed partial class UserAdministration(
    AuthenticationDbContext context,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<string>> roleManager,
    TimeProvider timeProvider,
    ILogger<UserAdministration> logger) : IUserAdministration
{
    private const string UserNotFound = "The user was not found.";
    private const string InvalidRequest = "The request is invalid.";
    private const string RolesMissing = "One or more roles do not exist.";
    private const string LastAdministrator = "The operation would leave no enabled administrator.";

    public async Task<IReadOnlyList<UserView>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await context.Users
            .AsNoTracking()
            .OrderBy(user => user.NormalizedEmail)
            .ToListAsync(cancellationToken);
        var rolesByUser = await LoadRolesAsync(null, cancellationToken);

        return [.. users.Select(user => ToView(user, rolesByUser))];
    }

    public async Task<AdministrationResult<UserView>> FindAsync(string id, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return user is null
            ? NotFound()
            : new AdministrationResult<UserView>(ToView(user, await LoadRolesAsync(id, cancellationToken)));
    }

    public async Task<AdministrationResult<UserView>> CreateAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One transaction: SQLite takes its write lock first, and a refusal leaves no partial state.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var roles = await ResolveRolesAsync(command.Roles);
        if (roles is null)
        {
            return new AdministrationResult<UserView>(AdministrationError.Invalid, RolesMissing);
        }

        var roleNames = roles.Select(role => role.Name!).ToList();

        var id = Guid.NewGuid().ToString();
        var user = new ApplicationUser { Id = id, UserName = id, Email = command.Email, IsEnabled = command.Enabled };

        var created = await SaveAsync(() => userManager.CreateAsync(user, command.Password));
        if (!created.Succeeded)
        {
            return Failure(created);
        }

        if (roleNames.Count > 0)
        {
            var assigned = await SaveAsync(() => userManager.AddToRolesAsync(user, roleNames));
            if (!assigned.Succeeded)
            {
                return Failure(assigned);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        SecurityEvents.UserCreated(logger, id, roleNames.Count, now, TraceId(), SpanId());
        foreach (var role in roleNames)
        {
            SecurityEvents.UserRoleAssigned(logger, id, role, now, TraceId(), SpanId());
        }

        return await FindAsync(id, cancellationToken);
    }

    public async Task<AdministrationResult<UserView>> UpdateEmailAsync(
        string id,
        string email,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        // Re-submitting the user's own email (in any letter case) is not a conflict with itself.
        if (!string.Equals(userManager.NormalizeEmail(email), user.NormalizedEmail, StringComparison.Ordinal))
        {
            var updated = await SaveAsync(() => userManager.SetEmailAsync(user, email));
            if (!updated.Succeeded)
            {
                return Failure(updated);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return await FindAsync(id, cancellationToken);
    }

    public async Task<AdministrationResult<UserView>> SetEnabledAsync(
        string id,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var revoked = 0;
        var changed = user.IsEnabled != enabled;
        if (changed)
        {
            if (!enabled)
            {
                var holdsAdministratorRole = await context.UserRoles.AnyAsync(
                    assignment => assignment.UserId == id && assignment.RoleId == DatabaseInitializer.AdministratorRoleId,
                    cancellationToken);
                var enabledAdministrators = await CountEnabledAdministratorsAsync(cancellationToken);

                if (!AdministratorContinuity.Permits(
                        isEnabledAdministratorNow: user.IsEnabled && holdsAdministratorRole,
                        remainsEnabledAdministrator: false,
                        enabledAdministratorCount: enabledAdministrators))
                {
                    return new AdministrationResult<UserView>(AdministrationError.Conflict, LastAdministrator);
                }
            }

            // Only the enabled state changes: no password, stamp, email, or role is touched.
            user.IsEnabled = enabled;
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return Failure(updated);
            }

            // Disabling ends every renewable session in the same transaction; enabling restores none.
            if (!enabled)
            {
                revoked = await RevokeActiveFamiliesAsync(id, SessionRevocationReason.UserDisabled, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        if (changed)
        {
            if (enabled)
            {
                SecurityEvents.UserEnabled(logger, id, timeProvider.GetUtcNow(), TraceId(), SpanId());
            }
            else
            {
                SecurityEvents.UserDisabled(logger, id, timeProvider.GetUtcNow(), TraceId(), SpanId());
            }
        }

        if (revoked > 0)
        {
            LogSessionsRevoked(logger, id, revoked, nameof(SessionRevocationReason.UserDisabled), timeProvider.GetUtcNow(), Activity.Current?.TraceId.ToString(), Activity.Current?.SpanId.ToString());
        }

        return await FindAsync(id, cancellationToken);
    }

    public async Task<AdministrationResult<int>> RevokeSessionsAsync(string id, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (await userManager.FindByIdAsync(id) is null)
        {
            return new AdministrationResult<int>(AdministrationError.NotFound, UserNotFound);
        }

        var revoked = await RevokeActiveFamiliesAsync(id, SessionRevocationReason.Administrator, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogSessionsRevoked(logger, id, revoked, nameof(SessionRevocationReason.Administrator), timeProvider.GetUtcNow(), Activity.Current?.TraceId.ToString(), Activity.Current?.SpanId.ToString());

        return new AdministrationResult<int>(revoked);
    }

    /// <summary>Revokes the user's active families inside the caller's transaction; returns how many changed.</summary>
    private Task<int> RevokeActiveFamiliesAsync(string userId, SessionRevocationReason reason, CancellationToken cancellationToken) =>
        SessionFamilyRevocation.RevokeActiveAsync(context, userId, timeProvider.GetUtcNow(), reason, keepFamilyId: null, cancellationToken);

    [LoggerMessage(LogLevel.Information, "Renewable sessions of user {UserId} revoked ({Count} families, reason {Reason}) at {OccurredAtUtc:O}; trace {TraceId}, span {SpanId}.")]
    private static partial void LogSessionsRevoked(ILogger logger, string userId, int count, string reason, DateTimeOffset occurredAtUtc, string? traceId, string? spanId);

    private Task<int> CountEnabledAdministratorsAsync(CancellationToken cancellationToken) =>
        (from assignment in context.UserRoles
         join user in context.Users on assignment.UserId equals user.Id
         where assignment.RoleId == DatabaseInitializer.AdministratorRoleId && user.IsEnabled
         select user.Id).CountAsync(cancellationToken);

    public async Task<AdministrationResult<UserView>> ReplaceRolesAsync(
        string id,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var target = await ResolveRolesAsync(roles);
        if (target is null)
        {
            return new AdministrationResult<UserView>(AdministrationError.Invalid, RolesMissing);
        }

        var holdsAdministratorRole = await context.UserRoles.AnyAsync(
            assignment => assignment.UserId == id && assignment.RoleId == DatabaseInitializer.AdministratorRoleId,
            cancellationToken);
        var keepsAdministratorRole = target.Exists(role => role.Id == DatabaseInitializer.AdministratorRoleId);

        if (!AdministratorContinuity.Permits(
                isEnabledAdministratorNow: user.IsEnabled && holdsAdministratorRole,
                remainsEnabledAdministrator: user.IsEnabled && keepsAdministratorRole,
                enabledAdministratorCount: await CountEnabledAdministratorsAsync(cancellationToken)))
        {
            return new AdministrationResult<UserView>(AdministrationError.Conflict, LastAdministrator);
        }

        var current = await userManager.GetRolesAsync(user);
        var targetNames = target.Select(role => role.Name!).ToList();
        var toRemove = current.Where(name => !targetNames.Contains(name, StringComparer.Ordinal)).ToList();
        var toAdd = targetNames.Where(name => !current.Contains(name, StringComparer.Ordinal)).ToList();

        if (toRemove.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(user, toRemove);
            if (!removed.Succeeded)
            {
                return Failure(removed);
            }
        }

        if (toAdd.Count > 0)
        {
            var added = await userManager.AddToRolesAsync(user, toAdd);
            if (!added.Succeeded)
            {
                return Failure(added);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var committedAt = timeProvider.GetUtcNow();
        foreach (var role in toRemove)
        {
            SecurityEvents.UserRoleRemoved(logger, id, role, committedAt, TraceId(), SpanId());
        }

        foreach (var role in toAdd)
        {
            SecurityEvents.UserRoleAssigned(logger, id, role, committedAt, TraceId(), SpanId());
        }

        return await FindAsync(id, cancellationToken);
    }

    private static string? TraceId() => Activity.Current?.TraceId.ToString();

    private static string? SpanId() => Activity.Current?.SpanId.ToString();

    /// <summary>Resolves distinct existing roles after name normalization; <see langword="null"/> when any is missing.</summary>
    private async Task<List<IdentityRole<string>>?> ResolveRolesAsync(IReadOnlyList<string> requested)
    {
        var resolved = new Dictionary<string, IdentityRole<string>>(StringComparer.Ordinal);

        foreach (var name in requested)
        {
            var role = await roleManager.FindByNameAsync(name);
            if (role?.Name is null)
            {
                return null;
            }

            resolved[role.Id] = role;
        }

        return [.. resolved.Values];
    }

    private async Task<Dictionary<string, List<(string Normalized, string Name)>>> LoadRolesAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        var query = from userRole in context.UserRoles.AsNoTracking()
                    join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                    where userId == null || userRole.UserId == userId
                    select new { userRole.UserId, role.NormalizedName, role.Name };

        var rows = await query.ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.UserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => (row.NormalizedName ?? string.Empty, row.Name ?? string.Empty)).ToList());
    }

    private UserView ToView(ApplicationUser user, Dictionary<string, List<(string Normalized, string Name)>> rolesByUser)
    {
        var roles = rolesByUser.TryGetValue(user.Id, out var assigned)
            ? assigned.OrderBy(role => role.Normalized, StringComparer.Ordinal).Select(role => role.Name).ToList()
            : [];
        var lockedOut = user.LockoutEnd is { } end && end > timeProvider.GetUtcNow();

        return new UserView(
            user.Id,
            user.Email ?? string.Empty,
            user.IsEnabled,
            lockedOut,
            lockedOut ? user.LockoutEnd!.Value.UtcDateTime : null,
            roles);
    }

    /// <summary>Runs an Identity write, turning a unique-index race into a duplicate error.</summary>
    private static async Task<IdentityResult> SaveAsync(Func<Task<IdentityResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (DbUpdateException)
        {
            return IdentityResult.Failed(new IdentityError { Code = "DuplicateEmail" });
        }
    }

    private static AdministrationResult<UserView> NotFound() =>
        new(AdministrationError.NotFound, UserNotFound);

    /// <summary>Maps Identity error codes to a fixed outcome; Identity descriptions never leave this method.</summary>
    private static AdministrationResult<UserView> Failure(IdentityResult result)
    {
        var codes = result.Errors.Select(error => error.Code).ToList();

        if (codes.Exists(code => code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return new AdministrationResult<UserView>(AdministrationError.Conflict, "The email is already in use.");
        }

        if (codes.Exists(code => code.StartsWith("Password", StringComparison.Ordinal)))
        {
            return new AdministrationResult<UserView>(
                AdministrationError.Invalid,
                "The password does not satisfy the password policy.");
        }

        return new AdministrationResult<UserView>(AdministrationError.Invalid, InvalidRequest);
    }
}
