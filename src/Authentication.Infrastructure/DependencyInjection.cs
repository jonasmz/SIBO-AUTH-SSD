using Authentication.Application.Features.Login;
using Authentication.Application.Features.PasswordRecovery;
using Authentication.Application.Features.Passwords;
using Authentication.Application.Features.Roles;
using Authentication.Application.Features.Sessions;
using Authentication.Application.Features.Users;
using Authentication.Infrastructure.Email;
using Authentication.Infrastructure.Health;
using Authentication.Infrastructure.Identity;
using Authentication.Infrastructure.Logging;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Security;
using Authentication.Infrastructure.Sessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Authentication.Infrastructure;

public static class DependencyInjection
{
    private const int InitialMaxFailedAccessAttempts = 5;
    private static readonly TimeSpan InitialLockoutTimeSpan = TimeSpan.FromMinutes(15);

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

        var smtpOptions = SmtpOptions.FromConfiguration(configuration);
        var dataProtectionOptions = new DataProtectionStorageOptions
        {
            KeysPath = configuration[$"{DataProtectionStorageOptions.SectionName}:KeysPath"] ?? string.Empty
        };

        Validate(jwtOptions, sqliteOptions, refreshOptions);
        ValidateRecoveryInfrastructure(smtpOptions, dataProtectionOptions);
        ValidateLockoutOverrides(configuration);

        // Persistent log files (Technical Constraints §15.6): the directory must be an existing, writable mount.
        var fileLoggerOptions = PersistentFileLoggerOptions.FromConfiguration(configuration);
        var invalidLogSetting = fileLoggerOptions.FirstInvalidSetting();
        Require(invalidLogSetting is null, invalidLogSetting ?? PersistentFileLoggerOptions.SectionName);

        services.AddSingleton(Options.Create(jwtOptions));
        services.AddSingleton(Options.Create(sqliteOptions));
        services.AddSingleton(Options.Create(refreshOptions));
        services.AddSingleton(Options.Create(smtpOptions));
        services.AddSingleton(Options.Create(dataProtectionOptions));
        services.AddSingleton(Options.Create(fileLoggerOptions));
        services.AddSingleton<RefreshCredentialProtector>();
        services.AddScoped<IRenewableSessionStore, RenewableSessionStore>();
        services.AddScoped<IRefreshSessionRotation, RenewableSessionStore>();
        services.AddScoped<IRefreshSessionLookup, RenewableSessionStore>();
        services.AddScoped<ISessionFamilyRevocation, RenewableSessionStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InitializationState>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<SqliteHealthCheck>();

        services.AddSingleton<RsaSigningKey>();
        services.AddAdministrativeAuthentication();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddScoped<IIdentityCredentialValidator, IdentityCredentialValidator>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<RefreshSessionHandler>();
        services.AddScoped<LogoutSessionHandler>();
        services.AddScoped<IUserAdministration, UserAdministration>();
        services.AddScoped<IRoleAdministration, RoleAdministration>();
        services.AddScoped<IPasswordChange, PasswordChange>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IPasswordRecovery, PasswordRecovery>();
        services.AddScoped<ForgotPasswordHandler>();

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

                // Initial lockout (SRS NFR-SEC-BF-002/003). Identity's own default span is 5 minutes, so
                // the 15 are set explicitly; the Identity section bound below can still override both.
                options.Lockout.MaxFailedAccessAttempts = InitialMaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = InitialLockoutTimeSpan;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<string>>()
            .AddEntityFrameworkStores<AuthenticationDbContext>()
            // Only the Data Protection provider issues reset tokens; the email, phone and
            // authenticator providers of AddDefaultTokenProviders are not needed.
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(TokenOptions.DefaultProvider);

        // The key ring lives in operator-controlled storage that outlives the container and the
        // Compose project, so outstanding reset tokens survive restart, recreation and `down -v`.
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionOptions.KeysPath))
            .SetApplicationName("Authentication.Api");

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

    /// <summary>Fails startup, naming only the setting, when an explicit lockout override is not a positive value.</summary>
    private static void ValidateLockoutOverrides(IConfiguration configuration)
    {
        const string countSetting = "Identity:Lockout:MaxFailedAccessAttempts";
        const string spanSetting = "Identity:Lockout:DefaultLockoutTimeSpan";

        var count = configuration[countSetting];
        Require(string.IsNullOrWhiteSpace(count) || (int.TryParse(count, out var attempts) && attempts >= 1), countSetting);

        var span = configuration[spanSetting];
        Require(string.IsNullOrWhiteSpace(span) || (TimeSpan.TryParse(span, out var lockout) && lockout > TimeSpan.Zero), spanSetting);
    }

    private static void ValidateRecoveryInfrastructure(SmtpOptions smtpOptions, DataProtectionStorageOptions dataProtectionOptions)
    {
        var invalidSmtpSetting = smtpOptions.FirstInvalidSetting();
        Require(invalidSmtpSetting is null, invalidSmtpSetting ?? SmtpOptions.SectionName);
        Require(
            DataProtectionStorageOptions.IsUsableDirectory(dataProtectionOptions.KeysPath),
            $"{DataProtectionStorageOptions.SectionName}:KeysPath");
    }

    private static void Require(bool isValid, string setting)
    {
        if (!isValid)
        {
            throw new InvalidOperationException($"Required configuration '{setting}' is missing or invalid.");
        }
    }
}
