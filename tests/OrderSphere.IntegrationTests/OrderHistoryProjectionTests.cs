using Azure.Messaging.ServiceBus;
using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Inbox;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Ordering.Infrastructure.Persistence;
using OrderSphere.Ordering.Worker.Workers;
using Xunit;

namespace OrderSphere.IntegrationTests;

/// <summary>
/// Verifies the A3 order-history CQRS read-model: the OrderHistoryProjector appends one
/// <c>order_history</c> row per OrderStatusChangedIntegrationEvent and is idempotent against
/// redelivery (inbox dedupe). Uses SQLite in-process (not EF InMemory) so the real inbox row
/// insert and read-model insert commit through one provider.
/// </summary>
public sealed class OrderHistoryProjectionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OrderingDbContext> _options;

    public OrderHistoryProjectionTests()
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

    private static OrderHistoryProjector NewProjector()
        => new(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<OrderHistoryProjector>.Instance);

    private static OrderStatusChangedIntegrationEvent StatusEvent(
        Guid orderId, string previous, string next) => new()
        {
            OrderId = orderId,
            CorrelationId = Guid.NewGuid(),
            PreviousStatus = previous,
            NewStatus = next,
            CustomerEmail = "customer@example.com"
        };

    private async Task ProjectAsync(OrderStatusChangedIntegrationEvent evt)
    {
        await using var ctx = NewContext();
        var inbox = new EfInboxStore<OrderingDbContext>(ctx);
        await NewProjector().ProjectAsync(evt, ctx, inbox, CancellationToken.None);
    }

    [Fact]
    public async Task Projects_a_row_with_the_event_fields()
    {
        var orderId = Guid.NewGuid();
        var evt = StatusEvent(orderId, "Pending", "Confirmed");

        await ProjectAsync(evt);

        await using var verify = NewContext();
        var row = await verify.OrderHistory.SingleAsync(e => e.OrderId == orderId);
        row.PreviousStatus.Should().Be("Pending");
        row.NewStatus.Should().Be("Confirmed");
        row.CustomerEmail.Should().Be("customer@example.com");
        row.CorrelationId.Should().Be(evt.CorrelationId);
        row.OccurredAt.Should().BeCloseTo(evt.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Redelivered_event_inserts_no_duplicate()
    {
        var evt = StatusEvent(Guid.NewGuid(), "Pending", "Confirmed");

        await ProjectAsync(evt);
        await ProjectAsync(evt); // redelivery: same event id

        await using var verify = NewContext();
        var count = await verify.OrderHistory.CountAsync(e => e.OrderId == evt.OrderId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task Distinct_transitions_for_one_order_build_a_timeline()
    {
        var orderId = Guid.NewGuid();

        await ProjectAsync(StatusEvent(orderId, "Pending", "Confirmed"));
        await ProjectAsync(StatusEvent(orderId, "Confirmed", "Shipped"));

        await using var verify = NewContext();
        var rows = await verify.OrderHistory
            .Where(e => e.OrderId == orderId)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Select(r => r.NewStatus).Should().ContainInOrder("Confirmed", "Shipped");
    }
}
