using Authentication.Api.Features.Users;
using Authentication.Infrastructure.Security;

namespace Authentication.Api.Features.Administration;

public static class AdministrationEndpoints
{
    /// <summary>
    /// Creates the single <c>/api/admin</c> group. Every administrative endpoint is mapped inside it,
    /// so it requires a valid token with the Administrator role by construction.
    /// </summary>
    public static IEndpointRouteBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/admin")
            .RequireAuthorization(JwtValidationRegistration.AdministratorPolicy);

        group.MapUserAdministrationEndpoints();

        return endpoints;
    }
}
