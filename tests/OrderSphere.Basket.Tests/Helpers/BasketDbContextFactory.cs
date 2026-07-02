namespace OrderSphere.Basket.Tests.Helpers;

/// <summary>
/// Creates an isolated <see cref="BasketDbContext"/> backed by the EF Core in-memory provider.
/// Each call produces a fresh database to prevent cross-test state leakage.
/// </summary>
internal static class BasketDbContextFactory
{
    internal static BasketDbContext Create(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<BasketDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new BasketDbContext(
            options, NullPublisher.Instance, NullCurrentUser.Instance, tenantContext ?? NullTenantContext.Instance);
    }
}
