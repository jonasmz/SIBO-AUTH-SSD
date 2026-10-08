using System.Net;
using System.Net.Http.Json;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.Persistence;
using Authentication.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Authentication.IntegrationTests.Scenarios;

/// <summary>
/// The "backup from an older version" edge case: a database migrated only up to the first migration stands in
/// for an older backup; starting the host on it applies the pending migrations without losing its data.
/// </summary>
public sealed class OlderBackupRestoreTests
{
    private const string OlderMigration = "20261008012606_InitialIdentity";
    private const string AdministratorPassword = "Older-Backup1!";
    private const string MemberEmail = "member@example.test";
    private const string MemberPassword = "Memb3r-Backup!";

    [Fact]
    public async Task StartingOnAnOlderBackupAppliesPendingMigrationsAndKeepsItsData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var resources = new Phase1TestResources();
        await CreateOlderBackupAsync(resources.ConnectionString, cancellationToken);

        await using var factory = new AuthenticationApiFactory(sharedResources: resources);
        using var client = factory.CreateClient();

        using (var ready = await client.GetAsync("/health/ready", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }

        // The administrator is the backup's own row: its password survived and no second one was bootstrapped.
        Assert.NotEmpty(await AdminTestSupport.LoginAsync(
            client, AdminTestSupport.AdministratorEmail, AdministratorPassword, cancellationToken));
        Assert.NotEmpty(await AdminTestSupport.LoginAsync(client, MemberEmail, MemberPassword, cancellationToken));
        using var initialPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = AdminTestSupport.AdministratorEmail, password = AdminTestSupport.AdministratorPassword },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, initialPassword.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(cancellationToken));
        Assert.Equal(2, await db.Users.CountAsync(cancellationToken));
        Assert.True(await db.Users.AllAsync(user => user.IsEnabled, cancellationToken));
    }

    private static async Task CreateOlderBackupAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<AuthenticationDbContext>().UseSqlite(connectionString).Options;

        await using (var context = new AuthenticationDbContext(options))
        {
            await context.GetService<IMigrator>().MigrateAsync(OlderMigration, cancellationToken);
            Assert.Equal([OlderMigration], await context.Database.GetAppliedMigrationsAsync(cancellationToken));
        }

        var hasher = new PasswordHasher<ApplicationUser>();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await InsertUserAsync(connection, hasher, DatabaseInitializer.AdministratorUserId, "admin", AdminTestSupport.AdministratorEmail, AdministratorPassword, cancellationToken);
        await InsertUserAsync(connection, hasher, "0e0e0e0e-0000-4000-8000-000000000001", "member", MemberEmail, MemberPassword, cancellationToken);

        await using var role = connection.CreateCommand();
        role.CommandText = """
            INSERT INTO AspNetRoles (Id, Name, NormalizedName) VALUES ($id, 'Administrator', 'ADMINISTRATOR');
            INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES ($user, $id);
            """;
        role.Parameters.AddWithValue("$id", DatabaseInitializer.AdministratorRoleId);
        role.Parameters.AddWithValue("$user", DatabaseInitializer.AdministratorUserId);
        await role.ExecuteNonQueryAsync(cancellationToken);
        SqliteConnection.ClearAllPools();
    }

    private static async Task InsertUserAsync(
        SqliteConnection connection,
        PasswordHasher<ApplicationUser> hasher,
        string id,
        string userName,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash,
                SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
            VALUES ($id, $userName, $normalizedUserName, $email, $normalizedEmail, 0, $hash, $stamp, $stamp, 0, 0, 1, 0)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$userName", userName);
        command.Parameters.AddWithValue("$normalizedUserName", userName.ToUpperInvariant());
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$normalizedEmail", email.ToUpperInvariant());
        command.Parameters.AddWithValue("$hash", hasher.HashPassword(new ApplicationUser(), password));
        command.Parameters.AddWithValue("$stamp", Guid.NewGuid().ToString("N"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
