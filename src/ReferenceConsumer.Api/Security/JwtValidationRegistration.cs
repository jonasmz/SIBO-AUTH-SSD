using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ReferenceConsumer.Api.Security;

public static class JwtValidationRegistration
{
    public const string AdministratorPolicy = "Administrator";

    private const string AdministratorRole = "Administrator";

    public static ConsumerJwtOptions AddConsumerJwtValidation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = ConsumerJwtOptions.Load(configuration);
        var signingKey = new RsaSecurityKey(LoadPublicKey(options.PublicKeyPath));

        services.AddSingleton(options);

        // No Authority or MetadataAddress: validation never contacts Authentication API
        // and never downloads metadata or signing keys.
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false;
                bearer.IncludeErrorDetails = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = signingKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = options.ClockSkew,
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization(authorization =>
            authorization.AddPolicy(AdministratorPolicy, policy => policy.RequireRole(AdministratorRole)));

        return options;
    }

    private static RSA LoadPublicKey(string path)
    {
        string pem;

        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Name the setting only; the path and the underlying message are never reported.
            throw new InvalidOperationException(
                $"Required configuration '{ConsumerJwtOptions.PublicKeyPathSetting}' does not reference a readable public key file.");
        }

        // A private key supplied by mistake must fail startup instead of being held in memory.
        if (pem.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw NotAPublicKey();
        }

        var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            rsa.Dispose();
            throw NotAPublicKey();
        }
    }

    private static InvalidOperationException NotAPublicKey() =>
        new($"Required configuration '{ConsumerJwtOptions.PublicKeyPathSetting}' does not reference an RSA public key PEM.");
}
