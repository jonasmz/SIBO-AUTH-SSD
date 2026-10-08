using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Authentication.Infrastructure.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuthenticationDbContext>
{
    public AuthenticationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthenticationDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new AuthenticationDbContext(options);
    }
}
