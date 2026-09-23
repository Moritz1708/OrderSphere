using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.BuildingBlocks.ValueObjects;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.Errors;
using OrderSphere.Ordering.Domain.OrderEvents;
using OrderSphere.Ordering.Domain.ValueObjects;

namespace OrderSphere.Ordering.Domain.Entities;

/// <summary>
/// The order write aggregate, event-sourced. State is never persisted directly: every mutation
/// raises an <see cref="IOrderEvent"/> that is appended to the order's stream and folded back into
/// memory on load. The read side (<c>OrderView</c>) is a projection of this stream.
/// </summary>
/// <remarks>
/// Identity (<see cref="Id"/>) lives outside the payloads — it is the stream key in the event
/// store, so it is supplied on rehydration rather than carried by <see cref="OrderCreated"/>.
/// <see cref="Version"/> counts the events folded so far (committed + uncommitted) and drives the
/// store's optimistic-concurrency check on append.
/// </remarks>
public sealed class Order : IAggregateRoot
{
    public OrderId Id { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public Address ShippingAddress { get; private set; } = null!;
    public OrderStatus Status { get; private set; } = OrderStatus.Created;
    public PaymentMethod PaymentMethod { get; private set; }
    public string? TrackingNumber { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string? CouponCode { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal ShippingCost { get; private set; }

    private readonly List<OrderItem> _items = [];
    public IReadOnlyCollection<OrderItem> Items => _items;

    /// <summary>Number of events folded into this aggregate (committed plus not-yet-appended).</summary>
    public int Version { get; private set; }

    private readonly List<IOrderEvent> _uncommitted = [];
    public IReadOnlyList<IOrderEvent> UncommittedEvents => _uncommitted;

    private Order()
    {
        Id = OrderId.Empty;
        CustomerId = CustomerId.Empty;
    }

    /// <summary>Places a new order, raising <see cref="OrderCreated"/>.</summary>
    public static Order Create(
        CustomerId customerId,
        Address shippingAddress,
        PaymentMethod paymentMethod,
        IEnumerable<OrderItem> items,
        Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(shippingAddress);

        var itemList = items?.ToList() ?? throw new ArgumentNullException(nameof(items));
        if (itemList.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(items));

        var order = new Order { Id = OrderId.New() };
        order.Raise(new OrderCreated(
            customerId.Value,
            new OrderAddressData(
                shippingAddress.FirstName, shippingAddress.LastName, shippingAddress.Street,
                shippingAddress.City, shippingAddress.PostalCode, shippingAddress.Country),
            (int)paymentMethod,
            itemList.Select(i => new OrderLineData(i.ProductId.Value, i.ProductName, i.Quantity, i.Price)).ToList(),
            correlationId,
            DateTime.UtcNow));
        return order;
    }

    /// <summary>Rebuilds an aggregate by folding its persisted event stream in order.</summary>
    public static Order Rehydrate(OrderId id, IEnumerable<IOrderEvent> events)
    {
        var order = new Order { Id = id };
        foreach (var @event in events)
        {
            order.Apply(@event);
            order.Version++;
        }
        return order;
    }

    /// <summary>Records a redeemed coupon and its discount. Set once during order creation.</summary>
    public void ApplyDiscount(string couponCode, decimal amount)
        => Raise(new CouponApplied(couponCode, amount, DateTime.UtcNow));

    /// <summary>Records the calculated shipping cost. Set once during order processing.</summary>
    public void SetShippingCost(decimal amount)
        => Raise(new ShippingCostSet(amount, DateTime.UtcNow));

    // Transitions are guarded here and only here. Apply stays unguarded because it also folds
    // persisted streams, which may already contain sequences these guards now reject.

    /// <summary>Marks the order as paid. Only a freshly created order can be confirmed.</summary>
    public Result Confirm(string trackingNumber)
    {
        if (Status is not OrderStatus.Created)
            return Result.Failure(OrderErrors.InvalidStatusTransition);

        Raise(new OrderConfirmed(trackingNumber, DateTime.UtcNow));
        return Result.Success();
    }

    public Result MarkShipped()
    {
        if (Status is not OrderStatus.Paid)
            return Result.Failure(OrderErrors.InvalidStatusTransition);

        Raise(new OrderShipped(DateTime.UtcNow));
        return Result.Success();
    }

    public Result MarkDelivered()
    {
        if (Status is not OrderStatus.Shipped)
            return Result.Failure(OrderErrors.InvalidStatusTransition);

        Raise(new OrderDelivered(DateTime.UtcNow));
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            return Result.Failure(OrderErrors.InvalidStatusTransition);

        Raise(new OrderCancelled(DateTime.UtcNow));
        return Result.Success();
    }

    /// <summary>Clears the uncommitted buffer once the store has persisted the events.</summary>
    public void MarkEventsCommitted() => _uncommitted.Clear();

    private void Raise(IOrderEvent @event)
    {
        Apply(@event);
        _uncommitted.Add(@event);
        Version++;
    }

    private void Apply(IOrderEvent @event)
    {
        switch (@event)
        {
            case OrderCreated e:
                CustomerId = CustomerId.From(e.CustomerId);
                ShippingAddress = new Address(
                    e.ShippingAddress.FirstName, e.ShippingAddress.LastName, e.ShippingAddress.Street,
                    e.ShippingAddress.City, e.ShippingAddress.PostalCode, e.ShippingAddress.Country);
                PaymentMethod = (PaymentMethod)e.PaymentMethod;
                CorrelationId = e.CorrelationId;
                _items.AddRange(e.Items.Select(i =>
                    new OrderItem(ProductId.From(i.ProductId), i.ProductName, Quantity.Of(i.Quantity), Money.Of(i.Price))));
                Status = OrderStatus.Created;
                break;
            case CouponApplied e:
                CouponCode = e.CouponCode;
                DiscountAmount = e.DiscountAmount;
                break;
            case ShippingCostSet e:
                ShippingCost = e.Amount;
                break;
            case OrderConfirmed e:
                TrackingNumber = e.TrackingNumber;
                Status = OrderStatus.Paid;
                break;
            case OrderShipped:
                Status = OrderStatus.Shipped;
                break;
            case OrderDelivered:
                Status = OrderStatus.Delivered;
                break;
            case OrderCancelled:
                Status = OrderStatus.Cancelled;
                break;
        }
    }
}
