using Authentication.Infrastructure.Security;

namespace Authentication.Api.Features.Administration;

public static class AdministrationEndpoints
{
    /// <summary>
    /// Creates the single <c>/api/admin</c> group. Every administrative endpoint is mapped inside it,
    /// so it requires a valid token with the Administrator role by construction.
    /// </summary>
    public static RouteGroupBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapGroup("/api/admin")
            .RequireAuthorization(JwtValidationRegistration.AdministratorPolicy);
    }
}
