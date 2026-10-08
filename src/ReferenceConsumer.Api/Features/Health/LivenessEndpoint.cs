namespace ReferenceConsumer.Api.Features.Health;

public static class LivenessEndpoint
{
    public static IEndpointRouteBuilder MapLivenessEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

        return endpoints;
    }
}
