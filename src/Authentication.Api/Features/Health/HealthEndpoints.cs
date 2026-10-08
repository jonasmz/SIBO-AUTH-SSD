using Authentication.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Authentication.Api.Features.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(HealthStatusResponse.Healthy));

        endpoints.MapGet("/health/ready", async (SqliteHealthCheck check, CancellationToken cancellationToken) =>
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext(), cancellationToken);

            return result.Status == HealthStatus.Healthy
                ? Results.Ok(HealthStatusResponse.Healthy)
                : Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Service Unavailable",
                    detail: "The service is not ready.");
        });

        return endpoints;
    }
}
