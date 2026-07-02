using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Infrastructure.Persistence;
using OrderSphere.Ordering.Worker.Workers;
using Xunit;
using OrderItemDto = OrderSphere.BuildingBlocks.Contracts.Events.OrderItemDto;
using ShippingAddressDto = OrderSphere.BuildingBlocks.Contracts.Events.ShippingAddressDto;

namespace OrderSphere.IntegrationTests;

/// <summary>
/// Covers the payment-result → Order status update hop:
/// <c>PaymentResultProcessor.ProcessPaymentResultAsync</c> consumes a
/// <see cref="PaymentProcessedIntegrationEvent"/> from the <c>payment-results</c> queue
/// and transitions the Order to Paid (success) or Cancelled (failure), enqueuing the
/// appropriate downstream outbox events.
///
/// Uses SQLite in-process because EF Core InMemory does not support ComplexProperty
/// (used by <c>OrderItem.Price: Money</c>), which causes a KeyNotFoundException at runtime.
/// </summary>
public sealed class PaymentResultToOrderFlowTests : IDisposable
{
    private static readonly Guid CustomerGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProductGuid = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OrderingDbContext> _options;

    public PaymentResultToOrderFlowTests()
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

    private static PaymentResultProcessor NewProcessor()
        => new(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<PaymentResultProcessor>.Instance);

    // Catalog reservation confirm/release succeed by default in these order-flow tests.
    private static ICatalogClient Catalog()
    {
        var catalog = Substitute.For<ICatalogClient>();
        catalog.ConfirmReservationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        catalog.ReleaseReservationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));
        return catalog;
    }

    private async Task<(Guid OrderId, Guid CorrelationId)> SeedOrderAsync()
    {
        var checkoutEvent = new CheckoutCartIntegrationEvent
        {
            CorrelationId = Guid.NewGuid(),
            CustomerId = CustomerGuid,
            CustomerEmail = "customer@example.com",
            CustomerName = "Max Mustermann",
            ShippingAddress = new ShippingAddressDto("Max", "Mustermann", "Hauptstr. 1", "Berlin", "10115", "DE"),
            PaymentMethod = PaymentMethod.CreditCard.ToString(),
            Items = [new OrderItemDto(ProductGuid, "Widget", 2, 9.99m)]
        };

        await using var ctx = NewContext();
        var result = await new OrderProcessor(
            Substitute.For<ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IShippingRateProvider>(),
            NullLogger<OrderProcessor>.Instance)
            .ProcessOrderAsync(checkoutEvent, ctx, CancellationToken.None);

        if (!result.IsSuccess) throw new InvalidOperationException($"ProcessOrderAsync failed: {result.ErrorMessage}");

        var outboxJson = await ctx.OutboxMessages
            .Where(m => m.Type == nameof(PaymentRequestedIntegrationEvent))
            .Select(m => m.Content)
            .SingleAsync();

        var paymentEvent = JsonSerializer.Deserialize<PaymentRequestedIntegrationEvent>(outboxJson);
        return (paymentEvent!.OrderId, checkoutEvent.CorrelationId);
    }

    private static PaymentProcessedIntegrationEvent SucceededEvent(Guid orderId, Guid correlationId) => new()
    {
        OrderId = orderId,
        CorrelationId = correlationId,
        Succeeded = true,
        CustomerEmail = "customer@example.com",
        PaymentMethod = "creditcard"
    };

    private static PaymentProcessedIntegrationEvent FailedEvent(Guid orderId, Guid correlationId) => new()
    {
        OrderId = orderId,
        CorrelationId = correlationId,
        Succeeded = false,
        FailureReason = "Card declined.",
        CustomerEmail = "customer@example.com",
        PaymentMethod = "creditcard"
    };

    [Fact]
    public async Task Successful_payment_transitions_order_to_Paid_and_enqueues_OrderPlaced_event()
    {
        var (orderId, correlationId) = await SeedOrderAsync();
        var evt = SucceededEvent(orderId, correlationId);

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = NewContext();
        var outcome = await NewProcessor().ProcessPaymentResultAsync(evt, ctx, inbox, Catalog(), deliveryCount: 1, CancellationToken.None);
        await ctx.SaveChangesAsync();

        outcome.Should().Be(PaymentResultProcessor.PaymentResultOutcome.Processed);

        await using var verify = NewContext();
        var updated = await verify.Orders.SingleAsync(o => o.Id == OrderId.From(orderId));
        updated.Status.Should().Be(OrderStatus.Paid);
        updated.TrackingNumber.Should().NotBeNullOrEmpty();

        var outboxMessages = await verify.OutboxMessages
            .Where(m => m.Type != nameof(PaymentRequestedIntegrationEvent))
            .ToListAsync();
        outboxMessages.Should().HaveCount(3);
        outboxMessages.Should().Contain(m => m.Type == nameof(OrderPlacedIntegrationEvent));
        outboxMessages.Should().Contain(m => m.Type == nameof(RealtimeNotificationEvent));
        outboxMessages.Should().Contain(m => m.Type == nameof(OrderStatusChangedIntegrationEvent));

        var orderPlacedJson = outboxMessages.Single(m => m.Type == nameof(OrderPlacedIntegrationEvent)).Content;
        var orderPlaced = JsonSerializer.Deserialize<OrderPlacedIntegrationEvent>(orderPlacedJson);
        orderPlaced!.OrderId.Should().Be(orderId);
        orderPlaced.TrackingNumber.Should().NotBeNullOrEmpty();
        orderPlaced.CustomerEmail.Should().Be("customer@example.com");

        await inbox.Received(1).MarkAsProcessedAsync(
            evt.Id, nameof(PaymentProcessedIntegrationEvent), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_payment_cancels_order_and_does_not_enqueue_OrderPlaced_event()
    {
        var (orderId, correlationId) = await SeedOrderAsync();
        var evt = FailedEvent(orderId, correlationId);

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = NewContext();
        var outcome = await NewProcessor().ProcessPaymentResultAsync(evt, ctx, inbox, Catalog(), deliveryCount: 1, CancellationToken.None);
        await ctx.SaveChangesAsync();

        outcome.Should().Be(PaymentResultProcessor.PaymentResultOutcome.Processed);

        await using var verify = NewContext();
        var updated = await verify.Orders.SingleAsync(o => o.Id == OrderId.From(orderId));
        updated.Status.Should().Be(OrderStatus.Cancelled);

        var outboxMessages = await verify.OutboxMessages
            .Where(m => m.Type != nameof(PaymentRequestedIntegrationEvent))
            .ToListAsync();
        outboxMessages.Should().HaveCount(2);
        outboxMessages.Should().NotContain(m => m.Type == nameof(OrderPlacedIntegrationEvent));
        outboxMessages.Should().Contain(m => m.Type == nameof(RealtimeNotificationEvent));
        outboxMessages.Should().Contain(m => m.Type == nameof(OrderStatusChangedIntegrationEvent));
    }

    [Fact]
    public async Task Duplicate_PaymentProcessed_event_is_idempotent()
    {
        var (orderId, correlationId) = await SeedOrderAsync();
        var evt = SucceededEvent(orderId, correlationId);

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(true);

        await using var ctx = NewContext();
        var outcome = await NewProcessor().ProcessPaymentResultAsync(evt, ctx, inbox, Catalog(), deliveryCount: 1, CancellationToken.None);

        outcome.Should().Be(PaymentResultProcessor.PaymentResultOutcome.AlreadyProcessed);

        await using var verify = NewContext();
        var unchanged = await verify.Orders.SingleAsync(o => o.Id == OrderId.From(orderId));
        unchanged.Status.Should().Be(OrderStatus.Created);
    }

    [Fact]
    public async Task Missing_order_returns_OrderNotFound()
    {
        var evt = SucceededEvent(Guid.NewGuid(), Guid.NewGuid());

        var inbox = Substitute.For<IInboxStore>();
        inbox.HasBeenProcessedAsync(evt.Id, Arg.Any<CancellationToken>()).Returns(false);

        await using var ctx = NewContext();
        var outcome = await NewProcessor().ProcessPaymentResultAsync(evt, ctx, inbox, Catalog(), deliveryCount: 1, CancellationToken.None);

        outcome.Should().Be(PaymentResultProcessor.PaymentResultOutcome.OrderNotFound);
    }
}
