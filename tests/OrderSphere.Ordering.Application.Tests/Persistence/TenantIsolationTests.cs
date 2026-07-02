using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Ordering.Application.Tests.Helpers;

namespace OrderSphere.Ordering.Application.Tests.Persistence;

/// <summary>
/// Verifies the org-level tenant query filter (ADR 0012): a context scoped to one tenant must
/// never see rows written by a context scoped to a different tenant, even against the same
/// underlying database — the isolation is enforced purely by the EF query filter, not by
/// per-tenant connection routing.
/// </summary>
public sealed class TenantIsolationTests
{
    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private static readonly Address Addr = new("Max", "Muster", "Str. 1", "Berlin", "10115", "DE");

    private static OrderView NewOrder() =>
        OrderView.Create(
            OrderId.New(), CustomerId.New(), Addr, PaymentMethod.CreditCard, Guid.NewGuid(),
            [new OrderItem(ProductId.New(), "Item", Quantity.Of(1), Money.Of(10m))],
            DateTime.UtcNow);

    [Fact]
    public async Task TenantA_CannotSeeOrders_WrittenByTenantB()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var tenantA = new FixedTenantContext(Guid.NewGuid());
        var tenantB = new FixedTenantContext(Guid.NewGuid());

        await using (var seedContext = OrderingDbContextFactory.Create(connection, tenantContext: tenantA))
        {
            seedContext.Database.EnsureCreated();
            seedContext.Orders.Add(NewOrder());
            await seedContext.SaveChangesAsync();
        }

        await using (var seedContext = OrderingDbContextFactory.Create(connection, tenantContext: tenantB))
        {
            seedContext.Orders.Add(NewOrder());
            await seedContext.SaveChangesAsync();
        }

        await using var asTenantA = OrderingDbContextFactory.Create(connection, tenantContext: tenantA);
        await using var asTenantB = OrderingDbContextFactory.Create(connection, tenantContext: tenantB);

        var ordersVisibleToA = await asTenantA.Orders.ToListAsync();
        var ordersVisibleToB = await asTenantB.Orders.ToListAsync();

        ordersVisibleToA.Should().ContainSingle();
        ordersVisibleToA.Single().TenantId.Should().Be(tenantA.TenantId);

        ordersVisibleToB.Should().ContainSingle();
        ordersVisibleToB.Single().TenantId.Should().Be(tenantB.TenantId);
    }

    [Fact]
    public async Task TenantFilter_ComposesWithSoftDeleteFilter()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var tenant = new FixedTenantContext(Guid.NewGuid());
        var order = NewOrder();

        await using (var seedContext = OrderingDbContextFactory.Create(connection, tenantContext: tenant))
        {
            seedContext.Database.EnsureCreated();
            seedContext.Orders.Add(order);
            await seedContext.SaveChangesAsync();
        }

        await using (var deleteContext = OrderingDbContextFactory.Create(connection, tenantContext: tenant))
        {
            var tracked = await deleteContext.Orders.SingleAsync(o => o.Id == order.Id);
            tracked.IsDeleted = true;
            await deleteContext.SaveChangesAsync();
        }

        await using var readContext = OrderingDbContextFactory.Create(connection, tenantContext: tenant);
        (await readContext.Orders.ToListAsync()).Should().BeEmpty();
    }
}
