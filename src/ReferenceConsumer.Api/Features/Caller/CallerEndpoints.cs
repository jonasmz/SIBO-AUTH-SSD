using System.Security.Claims;
using ReferenceConsumer.Api.Security;

namespace ReferenceConsumer.Api.Features.Caller;

public static class CallerEndpoints
{
    public static IEndpointRouteBuilder MapCallerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/caller", Describe).RequireAuthorization();

        return endpoints;
    }

    private static IResult Describe(ClaimsPrincipal user, ConsumerJwtOptions options)
    {
        var subject = user.FindFirstValue("sub") ?? string.Empty;
        var roles = user.FindAll("role").Select(claim => claim.Value).ToArray();

        return Results.Ok(new CallerIdentityResponse(options.ServiceName, subject, roles));
    }
}
