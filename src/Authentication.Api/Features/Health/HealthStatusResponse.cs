namespace Authentication.Api.Features.Health;

public sealed record HealthStatusResponse(string Status)
{
    public static HealthStatusResponse Healthy { get; } = new("healthy");
}
