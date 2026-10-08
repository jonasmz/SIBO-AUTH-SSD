using System.Data.Common;
using Authentication.Application.Features.Sessions;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Api.Features.Sessions;

public static class LogoutEndpoint
{
    public static IEndpointRouteBuilder MapLogoutEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/logout", HandleAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(HttpRequest request, LogoutSessionHandler handler, BrowserOriginValidator originValidator, RefreshCookieWriter cookieWriter, InitializationState state, CancellationToken cancellationToken)
    {
        if (!originValidator.IsTrusted(request)) return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "The request origin is not allowed.");
        if (!state.IsReady) return ServiceUnavailable();

        try
        {
            var raw = request.Cookies["auth_refresh"];
            var hasCredential = RefreshCredentialProtector.TryHash(raw ?? string.Empty, out var hash);
            await handler.HandleAsync(new LogoutSessionCommand(hasCredential ? hash : null), cancellationToken);
            cookieWriter.Clear(request.HttpContext.Response);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return ServiceUnavailable();
        }
    }

    private static IResult ServiceUnavailable() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable", detail: "The service is not ready.");
}
