using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Authentication.Infrastructure.Security;

/// <summary>
/// Validates, in-process, the access tokens Authentication API itself issues, with the same
/// parameters the consumer APIs apply. Used only by the administrative endpoints.
/// </summary>
public static class JwtValidationRegistration
{
    public const string AdministratorPolicy = "Administrator";

    private const string AdministratorRole = "Administrator";

    public static IServiceCollection AddAdministrativeAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // No Authority or MetadataAddress: validation never contacts another service.
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, RsaSigningKey>((bearer, jwt, signingKey) =>
            {
                var options = jwt.Value;

                bearer.MapInboundClaims = false;
                bearer.IncludeErrorDetails = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = signingKey.PublicKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                        await WriteProblemAsync(
                            context.Response,
                            StatusCodes.Status401Unauthorized,
                            "Unauthorized",
                            "Authentication is required.");
                    },
                    OnForbidden = context => WriteProblemAsync(
                        context.Response,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "The caller is not allowed to perform this operation.")
                };
            });

        services.AddAuthorization(authorization =>
            authorization.AddPolicy(AdministratorPolicy, policy => policy.RequireRole(AdministratorRole)));

        return services;
    }

    private static Task WriteProblemAsync(HttpResponse response, int status, string title, string detail)
    {
        response.StatusCode = status;

        return response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail },
            options: null,
            contentType: "application/problem+json");
    }
}
