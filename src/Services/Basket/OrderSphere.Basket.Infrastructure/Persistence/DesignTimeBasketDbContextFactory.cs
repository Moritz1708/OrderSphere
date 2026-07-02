using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.Basket.Infrastructure.Persistence;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations add) at design time.
/// </summary>
public sealed class DesignTimeBasketDbContextFactory : IDesignTimeDbContextFactory<BasketDbContext>
{
    public BasketDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BasketDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=basket-db;Username=postgres;Password=postgres")
            .Options;
        return new BasketDbContext(options, NullPublisher.Instance, NullCurrentUser.Instance, NullTenantContext.Instance);
    }
}
