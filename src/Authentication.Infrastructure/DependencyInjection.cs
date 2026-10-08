using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Security;
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

        return services;
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
