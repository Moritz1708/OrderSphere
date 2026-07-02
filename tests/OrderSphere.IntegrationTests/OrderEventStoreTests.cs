using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.BuildingBlocks.ValueObjects;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.ValueObjects;
using OrderSphere.Ordering.Infrastructure.EventSourcing;
using OrderSphere.Ordering.Infrastructure.Persistence;
using Xunit;

namespace OrderSphere.IntegrationTests;

/// <summary>
/// Verifies the A4 event-sourced order aggregate: appends persist the stream and a synchronous
/// read projection in one transaction, loads rebuild aggregate state by folding the stream, and
/// concurrent appends to the same stream collide on the (stream, version) key. Uses SQLite
/// in-process so events, projection, and the composite-key concurrency check share one provider.
/// </summary>
public sealed class OrderEventStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OrderingDbContext> _options;

    public OrderEventStoreTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private OrderingDbContext NewContext() =>
        new(_options, Substitute.For<IPublisher>(), NullCurrentUser.Instance, NullTenantContext.Instance);

    private static Order NewOrder() => Order.Create(
        CustomerId.New(),
        new Address("Max", "Muster", "Hauptstr. 1", "Berlin", "10115", "DE"),
        PaymentMethod.CreditCard,
        [new OrderItem(ProductId.New(), "Widget", Quantity.Of(2), Money.Of(9.99m))],
        Guid.NewGuid());

    /// <summary>Appends an aggregate's uncommitted events and commits the unit of work.</summary>
    private async Task SaveAsync(Order order)
    {
        await using var ctx = NewContext();
        await new OrderEventStore(ctx).AppendAsync(order, CancellationToken.None);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Append_then_Load_rebuilds_aggregate_state_from_the_stream()
    {
        var order = NewOrder();
        order.SetShippingCost(4.99m);
        order.Confirm("TRACK-1");
        await SaveAsync(order);

        await using var ctx = NewContext();
        var loaded = await new OrderEventStore(ctx).LoadAsync(order.Id, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(OrderStatus.Paid);
        loaded.TrackingNumber.Should().Be("TRACK-1");
        loaded.ShippingCost.Should().Be(4.99m);
        loaded.Version.Should().Be(3); // OrderCreated + ShippingCostSet + OrderConfirmed
        loaded.UncommittedEvents.Should().BeEmpty();
        loaded.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Load_of_unknown_stream_returns_null()
    {
        await using var ctx = NewContext();

        var loaded = await new OrderEventStore(ctx).LoadAsync(OrderId.New(), CancellationToken.None);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Stream_is_persisted_as_a_gap_free_one_based_sequence()
    {
        var order = NewOrder();      // v1
        order.SetShippingCost(4.99m); // v2
        await SaveAsync(order);

        await using var ctx = NewContext();
        var versions = await ctx.Set<OrderEventRecord>()
            .Where(e => e.StreamId == order.Id.Value)
            .OrderBy(e => e.Version)
            .Select(e => e.Version)
            .ToListAsync();

        versions.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Projection_mirrors_the_stream_including_the_status_timeline()
    {
        var order = NewOrder();
        order.Confirm("TRACK-1");
        order.MarkShipped();
        await SaveAsync(order);

        await using var ctx = NewContext();
        var view = await ctx.Orders.Include(o => o.Items).SingleAsync(o => o.Id == order.Id);

        view.Status.Should().Be(OrderStatus.Shipped);
        view.TrackingNumber.Should().Be("TRACK-1");
        view.Items.Should().ContainSingle();

        // The projection records one timeline entry per transition. Order is a read concern —
        // queries sort by OccurredAt — so assert completeness here and the chronological anchor.
        view.StatusHistory.Select(h => h.Status)
            .Should().BeEquivalentTo([OrderStatus.Created, OrderStatus.Paid, OrderStatus.Shipped]);
        view.StatusHistory.OrderBy(h => h.OccurredAt).Select(h => h.Status)
            .Should().ContainInOrder(OrderStatus.Created, OrderStatus.Paid, OrderStatus.Shipped);
    }

    [Fact]
    public async Task Append_across_loads_continues_the_version_sequence()
    {
        var order = NewOrder();
        await SaveAsync(order); // v1

        await using (var ctx = NewContext())
        {
            var store = new OrderEventStore(ctx);
            var loaded = await store.LoadAsync(order.Id, CancellationToken.None);
            loaded!.Confirm("TRACK-2"); // v2
            await store.AppendAsync(loaded, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var versions = await verify.Set<OrderEventRecord>()
            .Where(e => e.StreamId == order.Id.Value)
            .OrderBy(e => e.Version)
            .Select(e => e.Version)
            .ToListAsync();
        versions.Should().Equal(1, 2);
    }

    [Fact]
    public async Task Concurrent_appends_to_the_same_stream_collide_on_the_version_key()
    {
        var order = NewOrder();
        await SaveAsync(order); // committed at v1

        // Two writers load the same version and each append v2.
        await using var ctxA = NewContext();
        await using var ctxB = NewContext();
        var a = await new OrderEventStore(ctxA).LoadAsync(order.Id, CancellationToken.None);
        var b = await new OrderEventStore(ctxB).LoadAsync(order.Id, CancellationToken.None);

        a!.Confirm("TRACK-A");
        await new OrderEventStore(ctxA).AppendAsync(a, CancellationToken.None);
        await ctxA.SaveChangesAsync();

        b!.Cancel();
        await new OrderEventStore(ctxB).AppendAsync(b, CancellationToken.None);

        // Second writer's v2 duplicates the (stream, version) primary key.
        var act = async () => await ctxB.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Append_with_no_uncommitted_events_is_a_no_op()
    {
        var order = NewOrder();
        await SaveAsync(order);

        await using var ctx = NewContext();
        var loaded = await new OrderEventStore(ctx).LoadAsync(order.Id, CancellationToken.None);
        await new OrderEventStore(ctx).AppendAsync(loaded!, CancellationToken.None); // nothing pending

        ctx.ChangeTracker.HasChanges().Should().BeFalse();
    }
}
