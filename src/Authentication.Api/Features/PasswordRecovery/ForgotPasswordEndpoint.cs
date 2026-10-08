using Authentication.Api.Security;
using System.Data.Common;
using System.Text.Json;
using Authentication.Application.Features.PasswordRecovery;
using Authentication.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Authentication.Api.Features.PasswordRecovery;

public static class ForgotPasswordEndpoint
{
    public static IEndpointRouteBuilder MapForgotPasswordEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/forgot-password", HandleAsync).AllowAnonymous().RequireRateLimiting(RateLimitingRegistration.ForgotPassword);

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest httpRequest,
        ForgotPasswordHandler handler,
        RecoveryAddressLimiter addressLimiter,
        ILookupNormalizer normalizer,
        InitializationState state,
        CancellationToken cancellationToken)
    {
        // Validation happens before any lookup, so 400 never depends on whether an account exists.
        var request = await ReadRequestAsync(httpRequest, cancellationToken);
        if (request is null || !request.IsValid)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "The request is invalid.");
        }

        // Counted for every submitted address, existing or not, so a 429 never reveals an account; it runs
        // after validation (a 400 consumes nothing) and before any lookup.
        var attempt = addressLimiter.TryAcquire(normalizer.NormalizeEmail(request.Email!) ?? request.Email!);
        if (!attempt.Acquired)
        {
            await TooManyRequests.WriteAsync(httpRequest.HttpContext, RateLimitingRegistration.ForgotPasswordAddress, attempt.RetryAfter, logClientAddress: false);
            return Results.Empty;
        }

        if (!state.IsReady)
        {
            return ServiceUnavailable();
        }

        try
        {
            await handler.HandleAsync(request.Email!, cancellationToken);

            // The same response for existing, unknown, and disabled accounts, and for delivery failures.
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            return ServiceUnavailable();
        }
    }

    private static async Task<ForgotPasswordRequest?> ReadRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        if (!httpRequest.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await httpRequest.ReadFromJsonAsync<ForgotPasswordRequest>(cancellationToken);
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
