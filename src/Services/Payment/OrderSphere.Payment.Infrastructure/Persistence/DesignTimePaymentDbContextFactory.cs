using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.Payment.Infrastructure.Persistence;

/// <summary>
/// Used exclusively by EF Core tooling (dotnet ef migrations add).
/// Not registered in the production DI container.
/// </summary>
public sealed class DesignTimePaymentDbContextFactory : IDesignTimeDbContextFactory<PaymentDbContext>
{
    public PaymentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=payment-db;Username=postgres;Password=postgres")
            .Options;

        return new PaymentDbContext(options, NullPublisher.Instance, NullCurrentUser.Instance, NullTenantContext.Instance);
    }
}
