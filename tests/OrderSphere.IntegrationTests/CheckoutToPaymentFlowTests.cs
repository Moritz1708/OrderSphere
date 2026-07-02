using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Application.Features.Checkout;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.Events;
using OrderSphere.Ordering.Domain.ValueObjects;
using OrderSphere.Ordering.Infrastructure.Persistence;
using OrderSphere.Ordering.Worker.Workers;
using Xunit;
using OrderItemDto = OrderSphere.BuildingBlocks.Contracts.Events.OrderItemDto;
using ShippingAddressDto = OrderSphere.BuildingBlocks.Contracts.Events.ShippingAddressDto;

namespace OrderSphere.IntegrationTests;

/// <summary>
/// Exercises the documented checkout flow across two components stitched by the real
/// <see cref="CheckoutCartEvent"/> contract:
/// <c>CheckoutCartCommandHandler</c> (the API side that publishes to the <c>orders</c> queue)
/// → <c>OrderProcessor.ProcessOrderAsync</c> (the worker that persists the Order and enqueues
/// the <see cref="PaymentRequestedIntegrationEvent"/> outbox message for the <c>payment-requests</c> hop).
/// The Service Bus transport is replaced by an in-process capture so the test is deterministic
/// and infrastructure-free; the message payload that travels between the two stages is the
/// production contract, unchanged.
/// </summary>
public sealed class CheckoutToPaymentFlowTests
{
    private static readonly Guid CustomerGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid KeyboardId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CableId = Guid.Parse("33333333-3333-3333-3333-333333333333");


    private sealed class CapturingPublisher : IOrderingServiceBusPublisher
    {
        public List<CheckoutCartEvent> Published { get; } = [];

        public Task PublishCheckoutCartEventAsync(CheckoutCartEvent checkoutCartEvent, CancellationToken ct = default)
        {
            Published.Add(checkoutCartEvent);
            return Task.CompletedTask;
        }
    }

    private static Address NewAddress() =>
        new("Erika", "Mustermann", "Hauptstraße 1", "Berlin", "10115", "Deutschland");

    private static CheckoutCartCommand NewCommand(Guid idempotencyKey) =>
        new(
            CustomerId.From(CustomerGuid),
            "customer@example.com",
            "Erika Mustermann",
            NewAddress(),
            PaymentMethod.CreditCard,
            idempotencyKey);

    private static (ICatalogClient catalog, IBasketClient basket) WireSuccessfulClients()
    {
        var basket = Substitute.For<IBasketClient>();
        basket.GetCartAsync(CustomerGuid, Arg.Any<CancellationToken>())
            .Returns(Result<BasketCartInfo>.Success(new BasketCartInfo(CustomerGuid,
            [
                new BasketCartItemInfo(KeyboardId, 2),
                new BasketCartItemInfo(CableId, 1)
            ])));
        basket.ClearCartItemsAsync(CustomerGuid, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var catalog = Substitute.For<ICatalogClient>();
        catalog.GetProductByIdAsync(KeyboardId, Arg.Any<CancellationToken>())
            .Returns(Result<CatalogProductInfo>.Success(
                new CatalogProductInfo(KeyboardId, "Mechanical Keyboard", 79.50m, 50, true)));
        catalog.GetProductByIdAsync(CableId, Arg.Any<CancellationToken>())
            .Returns(Result<CatalogProductInfo>.Success(
                new CatalogProductInfo(CableId, "USB-C Cable", 9.99m, 200, true)));
        catalog.ReserveStockAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ReservationItem>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        catalog.ReleaseReservationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        return (catalog, basket);
    }

    private static CheckoutCartCommandHandler NewHandler(
        ICatalogClient catalog, IBasketClient basket, IOrderingServiceBusPublisher publisher,
        ICheckoutIdempotencyStore idempotency) =>
        new(catalog, basket, publisher, idempotency, NullLogger<CheckoutCartCommandHandler>.Instance);


    private static OrderingDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OrderingDbContext(
            options, Substitute.For<IPublisher>(), NullCurrentUser.Instance, NullTenantContext.Instance);
    }

    private static OrderProcessor NewProcessor() =>
        new(
            Substitute.For<Azure.Messaging.ServiceBus.ServiceBusClient>(),
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IShippingRateProvider>(),  // unconfigured → free shipping (0)
            NullLogger<OrderProcessor>.Instance);

    private static CheckoutCartIntegrationEvent ToIntegrationEvent(CheckoutCartEvent e) =>
        new()
        {
            CorrelationId = e.CorrelationId,
            CustomerId = e.CheckoutCart.CustomerId,
            CustomerEmail = e.CheckoutCart.CustomerEmail,
            CustomerName = e.CheckoutCart.CustomerName,
            ShippingAddress = new ShippingAddressDto(
                e.CheckoutCart.ShippingAddress.FirstName,
                e.CheckoutCart.ShippingAddress.LastName,
                e.CheckoutCart.ShippingAddress.Street,
                e.CheckoutCart.ShippingAddress.City,
                e.CheckoutCart.ShippingAddress.PostalCode,
                e.CheckoutCart.ShippingAddress.Country),
            PaymentMethod = e.CheckoutCart.PaymentMethod.ToString(),
            CouponCode = e.CheckoutCart.CouponCode,
            Items = e.Items.Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.Quantity, i.Price)).ToList()
        };


    [Fact]
    public async Task Checkout_publishes_event_that_worker_turns_into_order_and_payment_request()
    {
        var (catalog, basket) = WireSuccessfulClients();
        var publisher = new CapturingPublisher();
        var idempotency = new InMemoryCheckoutIdempotencyStore();

        // Stage 1 — checkout accepts and publishes to the 'orders' queue.
        var checkoutResult = await NewHandler(catalog, basket, publisher, idempotency)
            .Handle(NewCommand(Guid.NewGuid()), CancellationToken.None);

        checkoutResult.IsSuccess.Should().BeTrue();
        publisher.Published.Should().ContainSingle();
        var orderEvent = publisher.Published[0];
        orderEvent.CorrelationId.Should().Be(checkoutResult.Value);
        orderEvent.Items.Should().HaveCount(2);

        // Stock was reserved (not decremented) against the correlation id and the cart was cleared.
        await catalog.Received(1).ReserveStockAsync(
            checkoutResult.Value,
            Arg.Is<IReadOnlyList<ReservationItem>>(items => items.Count == 2),
            Arg.Any<CancellationToken>());
        await basket.Received(1).ClearCartItemsAsync(CustomerGuid, Arg.Any<CancellationToken>());

        // Stage 2 — the worker consumes the very event the API produced.
        await using var context = NewContext();
        var processResult = await NewProcessor().ProcessOrderAsync(ToIntegrationEvent(orderEvent), context, CancellationToken.None);
        processResult.IsSuccess.Should().BeTrue();

        // An Order materialised for the customer with both lines and the shared correlation id.
        // Projected (rather than fully materialised) to sidestep the EF InMemory provider's
        // owned-type shaping limitation for OrderItem.Price (Money); the line content is still
        // asserted via the payment total below.
        var order = await context.Orders
            .Where(o => o.CorrelationId == orderEvent.CorrelationId)
            .Select(o => new
            {
                o.Id,
                o.CustomerId,
                o.PaymentMethod,
                ItemCount = o.Items.Count,
                ProductNames = o.Items.Select(i => i.ProductName).ToList()
            })
            .SingleAsync();
        order.CustomerId.Should().Be(CustomerId.From(CustomerGuid));
        order.PaymentMethod.Should().Be(PaymentMethod.CreditCard);
        order.ItemCount.Should().Be(2);
        order.ProductNames.Should().BeEquivalentTo("Mechanical Keyboard", "USB-C Cable");

        // The payment-requests hop: a PaymentRequested outbox message carrying the order total.
        var outbox = await context.OutboxMessages.SingleAsync();
        outbox.Type.Should().Be(nameof(PaymentRequestedIntegrationEvent));

        var payment = JsonSerializer.Deserialize<PaymentRequestedIntegrationEvent>(outbox.Content);
        payment.Should().NotBeNull();
        payment!.OrderId.Should().Be(order.Id.Value);
        payment.CorrelationId.Should().Be(orderEvent.CorrelationId);
        payment.Amount.Should().Be(168.99m); // 2 * 79.50 + 1 * 9.99
        payment.Currency.Should().Be("EUR");
        payment.PaymentMethod.Should().Be(PaymentMethod.CreditCard.ToString());
        payment.CustomerEmail.Should().Be("customer@example.com");
    }

    [Fact]
    public async Task Worker_applies_coupon_reduces_payment_amount_and_redeems_once()
    {
        var (catalog, basket) = WireSuccessfulClients();
        var publisher = new CapturingPublisher();
        var idempotency = new InMemoryCheckoutIdempotencyStore();

        // Checkout carries a coupon code.
        await NewHandler(catalog, basket, publisher, idempotency)
            .Handle(NewCommand(Guid.NewGuid()) with { CouponCode = "SAVE20" }, CancellationToken.None);
        var orderEvent = publisher.Published[0];

        await using var context = NewContext();

        // Seed a flat €20 coupon for the worker to redeem.
        context.Coupons.Add(new Coupon("SAVE20", DiscountType.Flat, 20m,
            minSubtotal: null, validFrom: null, validUntil: null, maxRedemptions: null, isActive: true));
        await context.SaveChangesAsync();

        var processResult = await NewProcessor()
            .ProcessOrderAsync(ToIntegrationEvent(orderEvent), context, CancellationToken.None);
        processResult.IsSuccess.Should().BeTrue();

        // The order records the coupon and discount.
        var order = await context.Orders
            .Where(o => o.CorrelationId == orderEvent.CorrelationId)
            .Select(o => new { o.Id, o.DiscountAmount, o.CouponCode })
            .SingleAsync();
        order.DiscountAmount.Should().Be(20m);
        order.CouponCode.Should().Be("SAVE20");

        // The coupon was redeemed exactly once.
        var redeemed = await context.Coupons.Where(c => c.Code == "SAVE20")
            .Select(c => c.RedeemedCount).SingleAsync();
        redeemed.Should().Be(1);

        // Payment amount is the subtotal minus the discount: 168.99 − 20 = 148.99.
        var outbox = await context.OutboxMessages.SingleAsync();
        var payment = JsonSerializer.Deserialize<PaymentRequestedIntegrationEvent>(outbox.Content);
        payment!.Amount.Should().Be(148.99m);
    }

    [Fact]
    public async Task Empty_cart_short_circuits_checkout_and_publishes_nothing()
    {
        var (catalog, _) = WireSuccessfulClients();
        var basket = Substitute.For<IBasketClient>();
        basket.GetCartAsync(CustomerGuid, Arg.Any<CancellationToken>())
            .Returns(Result<BasketCartInfo>.Success(new BasketCartInfo(CustomerGuid, [])));
        var publisher = new CapturingPublisher();
        var idempotency = new InMemoryCheckoutIdempotencyStore();

        var result = await NewHandler(catalog, basket, publisher, idempotency)
            .Handle(NewCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
        await catalog.DidNotReceive().ReserveStockAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ReservationItem>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_reservation_publishes_nothing_and_does_not_release()
    {
        var (_, basket) = WireSuccessfulClients();
        var catalog = Substitute.For<ICatalogClient>();
        catalog.GetProductByIdAsync(KeyboardId, Arg.Any<CancellationToken>())
            .Returns(Result<CatalogProductInfo>.Success(
                new CatalogProductInfo(KeyboardId, "Mechanical Keyboard", 79.50m, 50, true)));
        catalog.GetProductByIdAsync(CableId, Arg.Any<CancellationToken>())
            .Returns(Result<CatalogProductInfo>.Success(
                new CatalogProductInfo(CableId, "USB-C Cable", 9.99m, 200, true)));
        // Reservation is rejected (insufficient availability) → nothing is published or released.
        catalog.ReserveStockAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<ReservationItem>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new Error("Catalog.InsufficientStock", "Insufficient stock.", ErrorType.Conflict)));

        var publisher = new CapturingPublisher();
        var idempotency = new InMemoryCheckoutIdempotencyStore();

        var result = await NewHandler(catalog, basket, publisher, idempotency)
            .Handle(NewCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        publisher.Published.Should().BeEmpty();
        await catalog.DidNotReceive().ReleaseReservationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Duplicate_idempotency_key_returns_same_correlation_without_republishing()
    {
        var (catalog, basket) = WireSuccessfulClients();
        var publisher = new CapturingPublisher();
        var idempotency = new InMemoryCheckoutIdempotencyStore();
        var handler = NewHandler(catalog, basket, publisher, idempotency);
        var key = Guid.NewGuid();

        var first = await handler.Handle(NewCommand(key), CancellationToken.None);
        var second = await handler.Handle(NewCommand(key), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(first.Value);
        publisher.Published.Should().ContainSingle(); // second request served from idempotency cache
    }

    [Fact]
    public async Task Worker_ignores_duplicate_correlation_id_and_creates_one_order()
    {
        var (catalog, basket) = WireSuccessfulClients();
        var publisher = new CapturingPublisher();
        var idempotency = new InMemoryCheckoutIdempotencyStore();

        await NewHandler(catalog, basket, publisher, idempotency)
            .Handle(NewCommand(Guid.NewGuid()), CancellationToken.None);
        var orderEvent = publisher.Published[0];

        await using var context = NewContext();
        var processor = NewProcessor();

        var integrationEvent = ToIntegrationEvent(orderEvent);
        var first = await processor.ProcessOrderAsync(integrationEvent, context, CancellationToken.None);
        var second = await processor.ProcessOrderAsync(integrationEvent, context, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        (await context.Orders.CountAsync(o => o.CorrelationId == orderEvent.CorrelationId)).Should().Be(1);
        (await context.OutboxMessages.CountAsync()).Should().Be(1);
    }
}
