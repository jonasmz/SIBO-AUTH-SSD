using Authentication.Application.Features.Login;
using Authentication.Application.Features.Roles;
using Authentication.Application.Features.Sessions;
using Authentication.Application.Features.Users;
using Authentication.Infrastructure.Health;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Security;
using Authentication.Infrastructure.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Authentication.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthenticationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var jwtOptions = new JwtOptions
        {
            Issuer = configuration[$"{JwtOptions.SectionName}:Issuer"] ?? string.Empty,
            Audience = configuration[$"{JwtOptions.SectionName}:Audience"] ?? string.Empty,
            AccessTokenLifetimeMinutes = ParseLifetime(
                configuration[$"{JwtOptions.SectionName}:AccessTokenLifetimeMinutes"]),
            PrivateKeyPath = configuration[$"{JwtOptions.SectionName}:PrivateKeyPath"] ?? string.Empty,
            ClockSkewSeconds = ParseClockSkew(configuration[$"{JwtOptions.SectionName}:ClockSkewSeconds"])
        };
        var sqliteOptions = new SqliteOptions
        {
            ConnectionString = configuration[$"{SqliteOptions.SectionName}:ConnectionString"] ?? string.Empty
        };
        var refreshOptions = new RefreshSessionOptions
        {
            LifetimeDays = ParseLifetime(configuration[$"{RefreshSessionOptions.SectionName}:LifetimeDays"] ?? "7"),
            FrontendOrigin = configuration["Security:FrontendOrigin"] ?? string.Empty
        };

        Validate(jwtOptions, sqliteOptions, refreshOptions);

        services.AddSingleton(Options.Create(jwtOptions));
        services.AddSingleton(Options.Create(sqliteOptions));
        services.AddSingleton(Options.Create(refreshOptions));
        services.AddSingleton<RefreshCredentialProtector>();
        services.AddScoped<IRenewableSessionStore, RenewableSessionStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InitializationState>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<SqliteHealthCheck>();

        services.AddSingleton<RsaSigningKey>();
        services.AddAdministrativeAuthentication();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<IIdentityCredentialValidator, IdentityCredentialValidator>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<IUserAdministration, UserAdministration>();
        services.AddScoped<IRoleAdministration, RoleAdministration>();

        services.AddDbContext<AuthenticationDbContext>(options =>
            options.UseSqlite(sqliteOptions.ConnectionString));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 5;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddRoles<IdentityRole<string>>()
            .AddEntityFrameworkStores<AuthenticationDbContext>();

        // Identity options (including the password policy) stay externally configurable.
        services.Configure<IdentityOptions>(configuration.GetSection("Identity"));

        return services;
    }

    public static Task InitializeAuthenticationInfrastructureAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Resolve the issuer first so an unusable signing key terminates startup.
        _ = services.GetRequiredService<IAccessTokenIssuer>();

        return services.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
    }

    private static int ParseLifetime(string? value)
    {
        return int.TryParse(value, out var lifetime) ? lifetime : 0;
    }

    private static int ParseClockSkew(string? value)
    {
        return int.TryParse(value, out var seconds) ? seconds : -1;
    }

    private static void Validate(JwtOptions jwtOptions, SqliteOptions sqliteOptions, RefreshSessionOptions refreshOptions)
    {
        Require(!string.IsNullOrWhiteSpace(sqliteOptions.ConnectionString), $"{SqliteOptions.SectionName}:ConnectionString");
        Require(!string.IsNullOrWhiteSpace(jwtOptions.Issuer), $"{JwtOptions.SectionName}:Issuer");
        Require(!string.IsNullOrWhiteSpace(jwtOptions.Audience), $"{JwtOptions.SectionName}:Audience");
        Require(jwtOptions.AccessTokenLifetimeMinutes > 0, $"{JwtOptions.SectionName}:AccessTokenLifetimeMinutes");
        Require(jwtOptions.ClockSkewSeconds is >= 0 and <= 60, $"{JwtOptions.SectionName}:ClockSkewSeconds");
        Require(RefreshSessionOptions.HasValidLifetime(refreshOptions.LifetimeDays), $"{RefreshSessionOptions.SectionName}:LifetimeDays");
        Require(RefreshSessionOptions.HasValidFrontendOrigin(refreshOptions.FrontendOrigin), "Security:FrontendOrigin");

        var keySetting = $"{JwtOptions.SectionName}:PrivateKeyPath";
        Require(!string.IsNullOrWhiteSpace(jwtOptions.PrivateKeyPath), keySetting);

        try
        {
            using var key = File.Open(
                jwtOptions.PrivateKeyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Name the setting only; the path and the underlying message are never reported.
            throw new InvalidOperationException(
                $"Required configuration '{keySetting}' does not reference a readable private key file.");
        }
    }

    private static void Require(bool isValid, string setting)
    {
        if (!isValid)
        {
            throw new InvalidOperationException($"Required configuration '{setting}' is missing or invalid.");
        }
    }
}
