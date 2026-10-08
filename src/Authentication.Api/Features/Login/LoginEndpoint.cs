using Authentication.Api.Security;
using System.Data.Common;
using System.Text.Json;
using Authentication.Application.Features.Login;
using Authentication.Api.Features.Sessions;
using Authentication.Infrastructure.Persistence;

namespace Authentication.Api.Features.Login;

public static class LoginEndpoint
{
    public static IEndpointRouteBuilder MapLoginEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/login", HandleAsync).AllowAnonymous().RequireRateLimiting(RateLimitingRegistration.Login);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest httpRequest,
        LoginHandler handler,
        RefreshCookieWriter refreshCookieWriter,
        InitializationState state,
        CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(httpRequest, cancellationToken);

        if (request is null || !request.IsValid)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "The request is invalid.");
        }

        if (!state.IsReady)
        {
            return ServiceUnavailable();
        }

        try
        {
            var outcome = await handler.HandleAsync(
                new LoginCommand(request.Email!, request.Password!),
                cancellationToken);

            if (outcome.Succeeded)
            {
                refreshCookieWriter.Write(httpRequest.HttpContext.Response, outcome.RefreshCredential!, outcome.RefreshExpiresAtUtc!.Value);
                return Results.Ok(new LoginResponse(
                    outcome.AccessToken!.EncodedToken,
                    outcome.AccessToken.ExpiresAtUtc.UtcDateTime));
            }
            return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Invalid credentials.");
        }
        catch (DbException)
        {
            return ServiceUnavailable();
        }
    }

    private static async Task<LoginRequest?> ReadRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!httpRequest.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await httpRequest.ReadFromJsonAsync<LoginRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult ServiceUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Service Unavailable",
        detail: "The service is not ready.");
}
