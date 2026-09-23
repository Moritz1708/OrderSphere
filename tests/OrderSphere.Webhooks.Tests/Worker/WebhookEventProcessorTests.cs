using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.Webhooks.Tests.Helpers;
using OrderSphere.Webhooks.Worker.Workers;

namespace OrderSphere.Webhooks.Tests.Worker;

/// <summary>
/// Subscriptions belong to a customer. An event must only produce deliveries for the
/// subscriptions of the customer it is about — the payload carries that customer's data.
/// </summary>
public sealed class WebhookEventProcessorTests
{
    private static readonly CustomerId CustomerA = CustomerId.New();
    private static readonly CustomerId CustomerB = CustomerId.New();

    private static string StatusChangedBody(Guid? customerId) =>
        JsonSerializer.Serialize(new OrderStatusChangedIntegrationEvent
        {
            OrderId = Guid.NewGuid(),
            PreviousStatus = "Pending",
            NewStatus = "Confirmed",
            CustomerEmail = "a@example.com",
            CustomerId = customerId
        });

    private static WebhookSubscription Subscription(CustomerId owner, params WebhookEventType[] events) =>
        new(owner, "https://example.com/hook", "secret", events);

    [Fact]
    public async Task StageDeliveries_OnlyMatchesSubscriptionsOfTheEventsCustomer()
    {
        await using var db = WebhooksDbContextFactory.Create();
        var ofA = Subscription(CustomerA, WebhookEventType.OrderStatusChanged);
        var ofB = Subscription(CustomerB, WebhookEventType.OrderStatusChanged);
        db.Subscriptions.AddRange(ofA, ofB);
        await db.SaveChangesAsync();

        var created = await WebhookEventProcessor.StageDeliveriesAsync(
            db, WebhookEventType.OrderStatusChanged, Guid.NewGuid(), StatusChangedBody(CustomerA.Value), default);
        await db.SaveChangesAsync();

        created.Should().Be(1);
        var deliveries = await db.Deliveries.ToListAsync();
        deliveries.Should().ContainSingle().Which.SubscriptionId.Should().Be(ofA.Id);
    }

    [Fact]
    public async Task StageDeliveries_EventWithoutCustomer_IsDeliveredToNobody()
    {
        await using var db = WebhooksDbContextFactory.Create();
        db.Subscriptions.Add(Subscription(CustomerA, WebhookEventType.OrderStatusChanged));
        await db.SaveChangesAsync();

        var created = await WebhookEventProcessor.StageDeliveriesAsync(
            db, WebhookEventType.OrderStatusChanged, Guid.NewGuid(), StatusChangedBody(null), default);
        await db.SaveChangesAsync();

        created.Should().Be(0);
        (await db.Deliveries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task StageDeliveries_IgnoresInactiveAndOtherEventSubscriptions()
    {
        await using var db = WebhooksDbContextFactory.Create();
        var inactive = Subscription(CustomerA, WebhookEventType.OrderStatusChanged);
        inactive.Deactivate();
        db.Subscriptions.AddRange(inactive, Subscription(CustomerA, WebhookEventType.OrderPlaced));
        await db.SaveChangesAsync();

        var created = await WebhookEventProcessor.StageDeliveriesAsync(
            db, WebhookEventType.OrderStatusChanged, Guid.NewGuid(), StatusChangedBody(CustomerA.Value), default);

        created.Should().Be(0);
    }
}
