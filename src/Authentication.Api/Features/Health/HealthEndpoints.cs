using Authentication.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Authentication.Api.Features.Health;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(HealthStatusResponse.Healthy))
            .WithSummary("Liveness: the process is running")
            .Produces<HealthStatusResponse>(StatusCodes.Status200OK);

        endpoints.MapGet("/health/ready", async (SqliteHealthCheck check, CancellationToken cancellationToken) =>
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext(), cancellationToken);

            return result.Status == HealthStatus.Healthy
                ? Results.Ok(HealthStatusResponse.Healthy)
                : Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Service Unavailable",
                    detail: "The service is not ready.");
        })
            .WithSummary("Readiness: the database is reachable and initialization succeeded")
            .Produces<HealthStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}
