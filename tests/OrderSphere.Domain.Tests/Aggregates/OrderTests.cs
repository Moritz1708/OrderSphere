using FluentAssertions;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.BuildingBlocks.ValueObjects;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.Errors;
using OrderSphere.Ordering.Domain.OrderEvents;
using OrderSphere.Ordering.Domain.ValueObjects;
using Xunit;

namespace OrderSphere.Domain.Tests.Aggregates;

public sealed class OrderTests
{
    private static readonly CustomerId Customer = CustomerId.New();
    private static readonly Address Addr = new("Max", "Muster", "Hauptstr. 1", "Berlin", "10115", "DE");
    private static readonly Guid Correlation = Guid.NewGuid();

    private static Order CreateOrder(IEnumerable<OrderItem>? items = null)
    {
        var defaultItems = items ?? [new OrderItem(ProductId.New(), "Widget", Quantity.Of(2), Money.Of(5m))];
        return Order.Create(Customer, Addr, PaymentMethod.CreditCard, defaultItems, Correlation);
    }

    [Fact]
    public void Create_SetsStatusCreated_AndAssignsId()
    {
        var order = CreateOrder();

        order.Status.Should().Be(OrderStatus.Created);
        order.Id.Should().NotBe(OrderId.Empty);
    }

    [Fact]
    public void Create_RaisesSingleOrderCreatedEvent_CarryingFullState()
    {
        var order = CreateOrder();

        order.UncommittedEvents.Should().ContainSingle()
            .Which.Should().BeOfType<OrderCreated>();

        var created = order.UncommittedEvents.OfType<OrderCreated>().Single();
        created.CorrelationId.Should().Be(Correlation);
        created.CustomerId.Should().Be(Customer.Value);
        created.PaymentMethod.Should().Be((int)PaymentMethod.CreditCard);
        created.Items.Should().ContainSingle();
        created.ShippingAddress.City.Should().Be("Berlin");
    }

    [Fact]
    public void Create_EmptyItems_Throws()
    {
        var act = () => CreateOrder([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ApplyDiscount_RaisesCouponApplied_AndUpdatesState()
    {
        var order = CreateOrder();

        order.ApplyDiscount("SAVE10", 3.50m);

        order.CouponCode.Should().Be("SAVE10");
        order.DiscountAmount.Should().Be(3.50m);
        order.UncommittedEvents.OfType<CouponApplied>().Should().ContainSingle();
    }

    [Fact]
    public void SetShippingCost_RaisesShippingCostSet_AndUpdatesState()
    {
        var order = CreateOrder();

        order.SetShippingCost(4.99m);

        order.ShippingCost.Should().Be(4.99m);
        order.UncommittedEvents.OfType<ShippingCostSet>().Should().ContainSingle();
    }

    [Fact]
    public void Confirm_SetsStatusPaid_AndRaisesOrderConfirmed()
    {
        var order = CreateOrder();

        order.Confirm("TRACK-001");

        order.Status.Should().Be(OrderStatus.Paid);
        order.TrackingNumber.Should().Be("TRACK-001");
        order.UncommittedEvents.OfType<OrderConfirmed>().Should().ContainSingle();
    }

    [Fact]
    public void MarkShipped_FromPaid_SetsStatusShipped()
    {
        var order = CreateOrder();
        order.Confirm("T");

        order.MarkShipped();

        order.Status.Should().Be(OrderStatus.Shipped);
        order.UncommittedEvents.OfType<OrderShipped>().Should().ContainSingle();
    }

    [Fact]
    public void MarkShipped_FromCreated_FailsWithoutRaisingEvent()
    {
        var order = CreateOrder();

        var result = order.MarkShipped();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidStatusTransition);
        order.Status.Should().Be(OrderStatus.Created);
        order.UncommittedEvents.OfType<OrderShipped>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled)]
    public void Confirm_FromNonCreatedStatus_FailsAndKeepsStatus(OrderStatus status)
    {
        var order = CreateOrder();
        if (status is OrderStatus.Paid) order.Confirm("T");
        if (status is OrderStatus.Cancelled) order.Cancel();

        var result = order.Confirm("T-2");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(OrderErrors.InvalidStatusTransition);
        order.Status.Should().Be(status);
    }

    [Fact]
    public void MarkDelivered_FromShipped_SetsStatusDelivered()
    {
        var order = CreateOrder();
        order.Confirm("T");
        order.MarkShipped();

        order.MarkDelivered();

        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void MarkDelivered_FromPaid_Fails()
    {
        var order = CreateOrder();
        order.Confirm("T");

        var result = order.MarkDelivered();

        result.Error.Should().Be(OrderErrors.InvalidStatusTransition);
        order.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void Cancel_FromCreated_SetsStatusCancelled()
    {
        var order = CreateOrder();

        order.Cancel();

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.UncommittedEvents.OfType<OrderCancelled>().Should().ContainSingle();
    }

    [Fact]
    public void Cancel_FromDelivered_Fails()
    {
        var order = CreateOrder();
        order.Confirm("T");
        order.MarkShipped();
        order.MarkDelivered();

        var result = order.Cancel();

        result.Error.Should().Be(OrderErrors.InvalidStatusTransition);
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Fails()
    {
        var order = CreateOrder();
        order.Cancel();

        var result = order.Cancel();

        result.Error.Should().Be(OrderErrors.InvalidStatusTransition);
        order.UncommittedEvents.OfType<OrderCancelled>().Should().ContainSingle();
    }

    [Fact]
    public void Rehydrate_FoldsHistoricSequencesTheGuardsNowReject()
    {
        // Streams written before the guards existed can contain Cancelled → Confirmed.
        // Loading them must still work; only new transitions are guarded.
        var source = CreateOrder();
        var stream = source.UncommittedEvents
            .Append(new OrderCancelled(DateTime.UtcNow))
            .Append(new OrderConfirmed("T", DateTime.UtcNow))
            .ToList();

        var rebuilt = Order.Rehydrate(source.Id, stream);

        rebuilt.Status.Should().Be(OrderStatus.Paid);
        rebuilt.Version.Should().Be(3);
    }

    [Fact]
    public void Version_CountsAllAppliedEvents()
    {
        var order = CreateOrder();   // OrderCreated
        order.SetShippingCost(4.99m); // ShippingCostSet
        order.Confirm("T");           // OrderConfirmed

        order.Version.Should().Be(3);
        order.UncommittedEvents.Should().HaveCount(3);
    }

    [Fact]
    public void MarkEventsCommitted_ClearsUncommitted_ButKeepsVersionAndState()
    {
        var order = CreateOrder();
        order.Confirm("T");

        order.MarkEventsCommitted();

        order.UncommittedEvents.Should().BeEmpty();
        order.Version.Should().Be(2);
        order.Status.Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public void Rehydrate_FromStream_ReproducesStateAndVersion_WithNoUncommittedEvents()
    {
        // Build a stream the way the store would persist it, then fold it back.
        var source = CreateOrder();
        source.ApplyDiscount("SAVE10", 3.50m);
        source.SetShippingCost(4.99m);
        source.Confirm("TRACK-9");
        source.MarkShipped();
        var stream = source.UncommittedEvents.ToList();

        var rebuilt = Order.Rehydrate(source.Id, stream);

        rebuilt.Id.Should().Be(source.Id);
        rebuilt.Status.Should().Be(OrderStatus.Shipped);
        rebuilt.TrackingNumber.Should().Be("TRACK-9");
        rebuilt.CouponCode.Should().Be("SAVE10");
        rebuilt.DiscountAmount.Should().Be(3.50m);
        rebuilt.ShippingCost.Should().Be(4.99m);
        rebuilt.CorrelationId.Should().Be(Correlation);
        rebuilt.Items.Should().ContainSingle();
        rebuilt.Version.Should().Be(stream.Count);
        rebuilt.UncommittedEvents.Should().BeEmpty();
    }
}
