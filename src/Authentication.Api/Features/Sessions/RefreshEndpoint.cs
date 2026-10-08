using Authentication.Api.Security;
using System.Data.Common;
using Authentication.Application.Features.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Api.Features.Sessions;

public static class RefreshEndpoint
{
    public static IEndpointRouteBuilder MapRefreshEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/refresh", HandleAsync).AllowAnonymous().RequireRateLimiting(RateLimitingRegistration.Refresh);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(HttpRequest request, RefreshSessionHandler handler, RefreshCredentialProtector protector, BrowserOriginValidator originValidator, RefreshCookieWriter refreshCookieWriter, InitializationState state, CancellationToken cancellationToken)
    {
        if (!originValidator.IsTrusted(request))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "The request origin is not allowed.");
        }
        if (!state.IsReady)
        {
            return ServiceUnavailable();
        }
        if (!RefreshCredentialProtector.TryHash(request.Cookies["auth_refresh"] ?? string.Empty, out var presentedHash))
        {
            return InvalidCredential();
        }

        var replacementCredential = protector.CreateRawCredential();
        if (!RefreshCredentialProtector.TryHash(replacementCredential, out var replacementHash))
        {
            return ServiceUnavailable();
        }

        try
        {
            var outcome = await handler.HandleAsync(new RefreshSessionCommand(presentedHash, replacementHash), cancellationToken);
            if (!outcome.Succeeded)
            {
                return InvalidCredential();
            }

            refreshCookieWriter.Write(request.HttpContext.Response, replacementCredential, outcome.RefreshExpiresAtUtc!.Value);
            return Results.Ok(new { accessToken = outcome.AccessToken!.EncodedToken, expiresAtUtc = outcome.AccessToken.ExpiresAtUtc.UtcDateTime });
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return ServiceUnavailable();
        }
    }

    private static IResult InvalidCredential() => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "Invalid refresh credential.");

    private static IResult ServiceUnavailable() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable", detail: "The service is not ready.");
}
