using Authentication.Application.Features.Login;
using Authentication.Infrastructure.Health;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Security;
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
            PrivateKeyPath = configuration[$"{JwtOptions.SectionName}:PrivateKeyPath"] ?? string.Empty
        };
        var sqliteOptions = new SqliteOptions
        {
            ConnectionString = configuration[$"{SqliteOptions.SectionName}:ConnectionString"] ?? string.Empty
        };

        Validate(jwtOptions, sqliteOptions);

        services.AddSingleton(Options.Create(jwtOptions));
        services.AddSingleton(Options.Create(sqliteOptions));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InitializationState>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<SqliteHealthCheck>();

        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<IIdentityCredentialValidator, IdentityCredentialValidator>();
        services.AddScoped<LoginHandler>();

        services.AddDbContext<AuthenticationDbContext>(options =>
            options.UseSqlite(sqliteOptions.ConnectionString));

        services.AddIdentityCore<IdentityUser<string>>(options =>
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

    private static void Validate(JwtOptions jwtOptions, SqliteOptions sqliteOptions)
    {
        if (string.IsNullOrWhiteSpace(sqliteOptions.ConnectionString) ||
            string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
            string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
            jwtOptions.AccessTokenLifetimeMinutes <= 0 ||
            string.IsNullOrWhiteSpace(jwtOptions.PrivateKeyPath) ||
            !File.Exists(jwtOptions.PrivateKeyPath))
        {
            throw new InvalidOperationException("Required Authentication API configuration is invalid.");
        }

        try
        {
            using var key = File.Open(
                jwtOptions.PrivateKeyPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
        }
        catch (IOException)
        {
            throw new InvalidOperationException("Required Authentication API configuration is invalid.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Required Authentication API configuration is invalid.");
        }
    }
}
