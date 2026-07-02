using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.Advisory.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by EF Core tooling (dotnet ef migrations add).
/// Not registered in the production DI container.
/// </summary>
public sealed class DesignTimeAdvisoryDbContextFactory : IDesignTimeDbContextFactory<AdvisoryDbContext>
{
    public AdvisoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AdvisoryDbContext>()
            .UseNpgsql("Host=localhost;Database=advisory-db;Username=postgres;Password=postgres")
            .Options;

        return new AdvisoryDbContext(options, NullCurrentUser.Instance, NullTenantContext.Instance);
    }
}
