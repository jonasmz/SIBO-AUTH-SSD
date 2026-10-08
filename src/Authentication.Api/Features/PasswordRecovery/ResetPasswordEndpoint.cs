using Authentication.Api.Security;
using System.Data.Common;
using System.Text.Json;
using Authentication.Application.Features.PasswordRecovery;
using Authentication.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Api.Features.PasswordRecovery;

public static class ResetPasswordEndpoint
{
    public static IEndpointRouteBuilder MapResetPasswordEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/reset-password", HandleAsync).AllowAnonymous().RequireRateLimiting(RateLimitingRegistration.ResetPassword);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest httpRequest,
        IPasswordRecovery recovery,
        InitializationState state,
        CancellationToken cancellationToken)
    {
        var request = await ReadRequestAsync(httpRequest, cancellationToken);
        if (request is null || !request.IsValid)
        {
            return Problem(StatusCodes.Status400BadRequest, "Bad Request", "The request is invalid.");
        }

        if (!state.IsReady)
        {
            return ServiceUnavailable();
        }

        try
        {
            var outcome = await recovery.ResetAsync(
                new ResetPasswordCommand(request.Email!, request.Token!, request.NewPassword!),
                cancellationToken);

            return outcome switch
            {
                ResetPasswordOutcome.Reset => Results.NoContent(),
                // Unknown email, disabled account, and every token failure are indistinguishable.
                ResetPasswordOutcome.InvalidToken => Problem(
                    StatusCodes.Status401Unauthorized, "Unauthorized", "Invalid or expired reset token."),
                ResetPasswordOutcome.InvalidNewPassword => Problem(
                    StatusCodes.Status400BadRequest, "Bad Request", "The password does not satisfy the password policy."),
                _ => Problem(StatusCodes.Status400BadRequest, "Bad Request", "The request is invalid.")
            };
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return ServiceUnavailable();
        }
    }

    private static async Task<ResetPasswordRequest?> ReadRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!httpRequest.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await httpRequest.ReadFromJsonAsync<ResetPasswordRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult Problem(int status, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail);

    private static IResult ServiceUnavailable() => Problem(
        StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "The service is not ready.");
}
