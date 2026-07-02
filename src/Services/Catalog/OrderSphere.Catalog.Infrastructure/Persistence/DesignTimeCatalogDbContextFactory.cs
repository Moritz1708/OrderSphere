using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.Catalog.Infrastructure.Persistence;

public sealed class DesignTimeCatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=catalog-db;Username=postgres;Password=postgres")
            .Options;
        return new CatalogDbContext(options, NullPublisher.Instance, NullCurrentUser.Instance, NullTenantContext.Instance);
    }
}
