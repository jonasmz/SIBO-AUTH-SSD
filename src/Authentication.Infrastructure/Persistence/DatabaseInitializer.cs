using Authentication.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Authentication.Infrastructure.Persistence;

public sealed partial class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    InitializationState state,
    ILogger<DatabaseInitializer> logger)
{
    public const string AdministratorRoleId = "7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f01";
    public const string AdministratorRoleName = "Administrator";
    public const string AdministratorUserId = "7f0b4a3e-5c1d-4e8a-9b6f-0a1c2d3e4f02";
    public const string AdministratorUserName = "admin";
    public const string AdministratorEmail = "admin@local.invalid";
    private const string InitialAdministratorPassword = "admin";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        state.MarkNotReady();

        var stage = "migration";

        try
        {
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<AuthenticationDbContext>();

            await context.Database.MigrateAsync(cancellationToken);
            stage = "bootstrap";
            await BootstrapAsync(context, services, cancellationToken);
        }
        catch (Exception exception)
        {
            var exceptionType = exception.GetType().Name;
            var sqliteErrorCode = exception is SqliteException sqliteException ? sqliteException.SqliteErrorCode : 0;
            LogInitializationFailed(logger, stage, exceptionType, sqliteErrorCode);
            throw new InvalidOperationException($"Authentication API initialization failed during {stage}.");
        }

        state.MarkReady();
    }

    private static async Task BootstrapAsync(
        AuthenticationDbContext context,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (await context.Users.AnyAsync(user => user.Id == AdministratorUserId, cancellationToken))
        {
            return;
        }

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<string>>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        if (!await context.Roles.AnyAsync(role => role.Id == AdministratorRoleId, cancellationToken))
        {
            Ensure(await roleManager.CreateAsync(new IdentityRole<string>
            {
                Id = AdministratorRoleId,
                Name = AdministratorRoleName
            }));
        }

        var administrator = new ApplicationUser
        {
            Id = AdministratorUserId,
            UserName = AdministratorUserName,
            Email = AdministratorEmail,
            IsEnabled = true
        };

        Ensure(await userManager.CreateAsync(administrator, InitialAdministratorPassword));
        Ensure(await userManager.AddToRoleAsync(administrator, AdministratorRoleName));

        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "Authentication API initialization failed during {Stage} ({ExceptionType}, SQLite error code {SqliteErrorCode}).")]
    private static partial void LogInitializationFailed(
        ILogger logger,
        string stage,
        string exceptionType,
        int sqliteErrorCode);

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Identity bootstrap operation failed.");
        }
    }
}
