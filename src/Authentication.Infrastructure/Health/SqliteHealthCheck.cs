using Authentication.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Authentication.Infrastructure.Health;

public sealed class SqliteHealthCheck(IServiceScopeFactory scopeFactory, InitializationState state) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!state.IsReady)
        {
            return HealthCheckResult.Unhealthy();
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();

            await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
