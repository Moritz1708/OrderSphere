using OrderSphere.BuildingBlocks.EventBus;

namespace OrderSphere.BuildingBlocks.Contracts.Events;

public sealed record OrderStatusChangedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string PreviousStatus { get; init; }
    public required string NewStatus { get; init; }
    public required string CustomerEmail { get; init; }

    /// <summary>
    /// Owner of the order. Webhook delivery is scoped to this customer's subscriptions;
    /// an event without it (published before the property existed) is delivered to nobody.
    /// </summary>
    public Guid? CustomerId { get; init; }
}
