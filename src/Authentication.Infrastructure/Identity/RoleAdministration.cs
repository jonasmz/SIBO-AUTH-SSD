using System.Data;
using Authentication.Application.Features.Administration;
using Authentication.Application.Features.Roles;
using Authentication.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Infrastructure.Identity;

public sealed class RoleAdministration(
    AuthenticationDbContext context,
    RoleManager<IdentityRole<string>> roleManager) : IRoleAdministration
{
    private const string RoleNotFound = "The role was not found.";
    private const string NameInUse = "The role name is already in use.";
    private const string AdministratorProtected = "The Administrator role cannot be renamed or deleted.";
    private const string RoleAssigned = "The role is assigned to one or more users.";

    public async Task<IReadOnlyList<RoleView>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await context.Roles
            .AsNoTracking()
            .OrderBy(role => role.NormalizedName)
            .ToListAsync(cancellationToken);

        return [.. roles.Select(ToView)];
    }

    public async Task<AdministrationResult<RoleView>> CreateAsync(string name, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var role = new IdentityRole<string> { Id = Guid.NewGuid().ToString(), Name = name };
        var created = await SaveAsync(() => roleManager.CreateAsync(role));
        if (!created.Succeeded)
        {
            return Failure(created);
        }

        await transaction.CommitAsync(cancellationToken);

        return new AdministrationResult<RoleView>(ToView(role));
    }

    public async Task<AdministrationResult<RoleView>> RenameAsync(string id, string name, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var role = await roleManager.FindByIdAsync(id);
        if (role is null)
        {
            return new AdministrationResult<RoleView>(AdministrationError.NotFound, RoleNotFound);
        }

        if (role.Id == DatabaseInitializer.AdministratorRoleId)
        {
            return new AdministrationResult<RoleView>(AdministrationError.Conflict, AdministratorProtected);
        }

        var renamed = await SaveAsync(async () =>
        {
            var named = await roleManager.SetRoleNameAsync(role, name);

            return named.Succeeded ? await roleManager.UpdateAsync(role) : named;
        });
        if (!renamed.Succeeded)
        {
            return Failure(renamed);
        }

        await transaction.CommitAsync(cancellationToken);

        return new AdministrationResult<RoleView>(ToView(role));
    }

    public async Task<AdministrationResult<RoleView>> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var role = await roleManager.FindByIdAsync(id);
        if (role is null)
        {
            return new AdministrationResult<RoleView>(AdministrationError.NotFound, RoleNotFound);
        }

        if (role.Id == DatabaseInitializer.AdministratorRoleId)
        {
            return new AdministrationResult<RoleView>(AdministrationError.Conflict, AdministratorProtected);
        }

        if (await context.UserRoles.AnyAsync(assignment => assignment.RoleId == id, cancellationToken))
        {
            return new AdministrationResult<RoleView>(AdministrationError.Conflict, RoleAssigned);
        }

        var removed = await roleManager.DeleteAsync(role);
        if (!removed.Succeeded)
        {
            return Failure(removed);
        }

        await transaction.CommitAsync(cancellationToken);

        return new AdministrationResult<RoleView>(ToView(role));
    }

    private static RoleView ToView(IdentityRole<string> role) => new(role.Id, role.Name ?? string.Empty);

    /// <summary>Runs an Identity write, turning a unique-index race into a duplicate-name error.</summary>
    private static async Task<IdentityResult> SaveAsync(Func<Task<IdentityResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (DbUpdateException)
        {
            return IdentityResult.Failed(new IdentityError { Code = "DuplicateRoleName" });
        }
    }

    /// <summary>Maps Identity error codes to a fixed outcome; Identity descriptions never leave this method.</summary>
    private static AdministrationResult<RoleView> Failure(IdentityResult result) =>
        result.Errors.Any(error => error.Code == "DuplicateRoleName")
            ? new AdministrationResult<RoleView>(AdministrationError.Conflict, NameInUse)
            : new AdministrationResult<RoleView>(AdministrationError.Invalid, "The request is invalid.");
}
