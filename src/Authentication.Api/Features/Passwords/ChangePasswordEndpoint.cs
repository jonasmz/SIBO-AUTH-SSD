using System.Data.Common;
using System.Text.Json;
using Authentication.Application.Features.Passwords;
using Authentication.Infrastructure.Persistence;
using Authentication.Infrastructure.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Api.Features.Passwords;

public static class ChangePasswordEndpoint
{
    public static IEndpointRouteBuilder MapChangePasswordEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/change-password", HandleAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest httpRequest,
        IPasswordChange passwordChange,
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

        // The account is always the token subject; the body carries no account identifier.
        var userId = httpRequest.HttpContext.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return InvalidCredentials();
        }

        // The refresh cookie only selects which session survives; it never authorizes the request.
        var hasCredential = RefreshCredentialProtector.TryHash(httpRequest.Cookies["auth_refresh"] ?? string.Empty, out var hash);

        try
        {
            var outcome = await passwordChange.ChangeAsync(
                new ChangePasswordCommand(userId, request.CurrentPassword!, request.NewPassword!, hasCredential ? hash : null),
                cancellationToken);

            return outcome switch
            {
                ChangePasswordOutcome.Changed => Results.NoContent(),
                ChangePasswordOutcome.InvalidCurrentPassword => InvalidCredentials(),
                ChangePasswordOutcome.InvalidNewPassword => Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "The password does not satisfy the password policy."),
                _ => Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "The request is invalid.")
            };
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return ServiceUnavailable();
        }
    }

    private static async Task<ChangePasswordRequest?> ReadRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!httpRequest.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await httpRequest.ReadFromJsonAsync<ChangePasswordRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult InvalidCredentials() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized",
        detail: "Invalid credentials.");

    private static IResult ServiceUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Service Unavailable",
        detail: "The service is not ready.");
}
