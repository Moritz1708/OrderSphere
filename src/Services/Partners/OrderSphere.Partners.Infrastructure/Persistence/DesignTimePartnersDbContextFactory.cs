using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.Partners.Infrastructure.Persistence;

/// <summary>
/// Used by EF Core tooling (dotnet ef migrations add) at design time.
/// </summary>
public sealed class DesignTimePartnersDbContextFactory : IDesignTimeDbContextFactory<PartnersDbContext>
{
    public PartnersDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PartnersDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=partners-db;Username=postgres;Password=postgres");
        return new PartnersDbContext(optionsBuilder.Options, NullTenantContext.Instance);
    }
}
