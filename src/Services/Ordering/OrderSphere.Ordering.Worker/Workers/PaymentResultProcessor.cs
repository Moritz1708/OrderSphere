using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.Services;
using OrderSphere.Ordering.Infrastructure.EventSourcing;
using OrderSphere.Ordering.Infrastructure.Persistence;

namespace OrderSphere.Ordering.Worker.Workers;

public sealed class PaymentResultProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentResultProcessor> logger) : BackgroundService
{
    private const string QueueName = "payment-results";

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += OnMessageReceived;
        _processor.ProcessErrorAsync += OnError;

        await _processor.StartProcessingAsync(stoppingToken);
        logger.ProcessorStarted(nameof(PaymentResultProcessor), QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.ProcessorStopped(nameof(PaymentResultProcessor));
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        logger.MessageReceived();

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<PaymentProcessedIntegrationEvent>();
            if (evt is null)
            {
                logger.MessageUndeserializable();
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid PaymentProcessedIntegrationEvent.");
                return;
            }

            messageScope.SetTenant(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var catalogClient = scope.ServiceProvider.GetRequiredService<ICatalogClient>();

            var outcome = await ProcessPaymentResultAsync(
                evt, context, inboxStore, catalogClient, (int)args.Message.DeliveryCount, args.CancellationToken);

            if (outcome == PaymentResultOutcome.OrderNotFound)
            {
                logger.LogWarning("Order {OrderId} not found for payment result. Dead-lettering.", evt.OrderId);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "OrderNotFound",
                    deadLetterErrorDescription: $"Order {evt.OrderId} not found.");
                return;
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.MessageProcessingFailed(ex);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    internal async Task<PaymentResultOutcome> ProcessPaymentResultAsync(
        PaymentProcessedIntegrationEvent evt,
        OrderingDbContext context,
        IInboxStore inboxStore,
        ICatalogClient catalogClient,
        int deliveryCount,
        CancellationToken ct)
    {
        if (await inboxStore.HasBeenProcessedAsync(evt.Id, ct))
        {
            logger.LogInformation("Event {EventId} already processed.", evt.Id);
            return PaymentResultOutcome.AlreadyProcessed;
        }

        var eventStore = new OrderEventStore(context);
        var order = await eventStore.LoadAsync(OrderId.From(evt.OrderId), ct);

        if (order is null)
        {
            return PaymentResultOutcome.OrderNotFound;
        }

        // Saga read-model: advanced in the same transaction as the order state change below.
        // Tracked, so the SaveChanges inside MarkAsProcessedAsync persists it atomically.
        var saga = await context.OrderSagas.FirstOrDefaultAsync(s => s.CorrelationId == evt.CorrelationId, ct);

        if (evt.Succeeded)
        {
            // Confirm the stock reservation (decrements on-hand stock) before persisting the
            // order state. Confirm is idempotent; on failure we throw so the message is retried
            // rather than leaving stock reserved-but-uncommitted for a paid order.
            var confirm = await catalogClient.ConfirmReservationAsync(order.CorrelationId, ct);
            if (confirm.IsFailure)
            {
                // Distinguish transient from non-recoverable failure so a network blip never
                // refunds an already-captured payment. A transient failure (catalog unavailable,
                // 5xx) is retried via Service Bus redelivery; a persistent outage escalates to the
                // dead-letter queue for operator intervention rather than auto-compensating. Only a
                // genuine 409 conflict (on-hand stock can no longer cover the reservation) is
                // non-recoverable — compensate deterministically with a refund.
                if (confirm.Error.Type != ErrorType.Conflict)
                    throw new InvalidOperationException(
                        $"Reservation confirm failed transiently (delivery {deliveryCount}) for order {order.Id} (correlation {order.CorrelationId}): {confirm.Error.Code}");

                return await CompensateConfirmationFailureAsync(evt, order, saga, catalogClient, context, eventStore, inboxStore, ct);
            }

            order.Confirm(TrackingNumberGenerator.Generate());
            saga?.MarkConfirmed();
            OrderingMetrics.OrdersConfirmed.Add(1);
            OrderingMetrics.RecordSagaTransition(nameof(SagaState.Confirmed));
            logger.LogInformation("Order {OrderId} confirmed after payment. TrackingNumber: {TrackingNumber}",
                order.Id, order.TrackingNumber);
        }
        else
        {
            order.Cancel();
            saga?.MarkCancelled(evt.FailureReason);
            OrderingMetrics.OrdersCancelled.Add(1);
            OrderingMetrics.RecordSagaTransition(nameof(SagaState.Cancelled));
            logger.LogWarning("Order {OrderId} cancelled due to payment failure: {Reason}",
                order.Id, evt.FailureReason);

            // Release the hold (best-effort: the TTL sweeper reclaims it if this call fails).
            var release = await catalogClient.ReleaseReservationAsync(order.CorrelationId, ct);
            if (release.IsFailure)
                logger.LogWarning("Reservation release failed for order {OrderId}; TTL sweeper will reclaim it.",
                    order.Id);
        }

        // Queue downstream events in the outbox — committed atomically with the order state
        // change and inbox record by the SaveChangesAsync inside MarkAsProcessedAsync.
        if (evt.Succeeded)
        {
            var orderPlacedEvent = new OrderPlacedIntegrationEvent
            {
                CorrelationId = evt.CorrelationId,
                OrderId = order.Id.Value,
                CustomerEmail = evt.CustomerEmail,
                CustomerName = $"{order.ShippingAddress.FirstName} {order.ShippingAddress.LastName}",
                TrackingNumber = order.TrackingNumber!,
                ShippingFirstName = order.ShippingAddress.FirstName,
                ShippingLastName = order.ShippingAddress.LastName,
                ShippingStreet = order.ShippingAddress.Street,
                ShippingCity = order.ShippingAddress.City,
                ShippingPostalCode = order.ShippingAddress.PostalCode,
                ShippingCountry = order.ShippingAddress.Country,
                Items = order.Items
                    .Select(i => new OrderPlacedItemDto(i.ProductName, i.Quantity, i.Price))
                    .ToList(),
                Total = order.Items.Sum(i => i.Price * i.Quantity) - order.DiscountAmount + order.ShippingCost
            };
            context.AddOutboxMessage(
                nameof(OrderPlacedIntegrationEvent),
                JsonSerializer.Serialize(orderPlacedEvent));
        }

        context.AddOutboxMessage(
            nameof(RealtimeNotificationEvent),
            JsonSerializer.Serialize(new RealtimeNotificationEvent
            {
                CorrelationId = evt.CorrelationId,
                UserId = order.CustomerId.ToString(),
                Type = evt.Succeeded ? "OrderConfirmed" : "OrderCancelled",
                Title = evt.Succeeded ? "Order Confirmed" : "Order Cancelled",
                Message = evt.Succeeded
                    ? $"Your order has been confirmed. Tracking number: {order.TrackingNumber}"
                    : $"Your order could not be processed: {evt.FailureReason ?? "Payment failed."}",
                OrderId = order.Id.Value
            }));

        context.AddOutboxMessage(
            nameof(OrderStatusChangedIntegrationEvent),
            JsonSerializer.Serialize(new OrderStatusChangedIntegrationEvent
            {
                CorrelationId = evt.CorrelationId,
                OrderId = order.Id.Value,
                PreviousStatus = "Pending",
                NewStatus = evt.Succeeded ? "Confirmed" : "Cancelled",
                CustomerEmail = evt.CustomerEmail
            }));

        // Stage the new order events and their read projection, then commit everything in one
        // SaveChangesAsync: event stream + projection + outbox rows + inbox mark — all atomic.
        await eventStore.AppendAsync(order, ct);
        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(PaymentProcessedIntegrationEvent), ct);

        return PaymentResultOutcome.Processed;
    }

    /// <summary>
    /// Compensates a captured-but-unconfirmable order: cancels the order, releases the reservation
    /// (best-effort), advances the saga to <see cref="SagaState.CompensationPending"/>, and queues an
    /// <see cref="OrderConfirmationFailedIntegrationEvent"/> so Payment refunds the capture. All writes
    /// commit atomically with the inbox mark in the single SaveChanges inside MarkAsProcessedAsync.
    /// </summary>
    private async Task<PaymentResultOutcome> CompensateConfirmationFailureAsync(
        PaymentProcessedIntegrationEvent evt,
        Order order,
        OrderSaga? saga,
        ICatalogClient catalogClient,
        OrderingDbContext context,
        OrderEventStore eventStore,
        IInboxStore inboxStore,
        CancellationToken ct)
    {
        var reason = "Reservation confirm conflict (stock can no longer cover the reservation); refunding payment.";

        order.Cancel();
        saga?.MarkCompensationPending(reason);
        OrderingMetrics.OrdersCancelled.Add(1);
        OrderingMetrics.RecordSagaTransition(nameof(SagaState.CompensationPending));
        logger.LogError(
            "Order {OrderId} confirmation failed after payment was captured; requesting refund. CorrelationId: {CorrelationId}",
            order.Id, evt.CorrelationId);

        // Release the reservation that was never committed (best-effort; the TTL sweeper backstops).
        var release = await catalogClient.ReleaseReservationAsync(order.CorrelationId, ct);
        if (release.IsFailure)
            logger.LogWarning("Reservation release failed for order {OrderId}; TTL sweeper will reclaim it.",
                order.Id);

        var amount = order.Items.Sum(i => i.Price * i.Quantity) - order.DiscountAmount + order.ShippingCost;

        context.AddOutboxMessage(
            nameof(OrderConfirmationFailedIntegrationEvent),
            JsonSerializer.Serialize(new OrderConfirmationFailedIntegrationEvent
            {
                CorrelationId = evt.CorrelationId,
                OrderId = order.Id.Value,
                Amount = amount,
                Currency = "EUR",
                Reason = reason,
                CustomerEmail = evt.CustomerEmail,
                PaymentMethod = evt.PaymentMethod
            }));

        context.AddOutboxMessage(
            nameof(RealtimeNotificationEvent),
            JsonSerializer.Serialize(new RealtimeNotificationEvent
            {
                CorrelationId = evt.CorrelationId,
                UserId = order.CustomerId.ToString(),
                Type = "OrderCancelled",
                Title = "Order Cancelled",
                Message = "Your payment is being refunded because your order could not be completed.",
                OrderId = order.Id.Value
            }));

        context.AddOutboxMessage(
            nameof(OrderStatusChangedIntegrationEvent),
            JsonSerializer.Serialize(new OrderStatusChangedIntegrationEvent
            {
                CorrelationId = evt.CorrelationId,
                OrderId = order.Id.Value,
                PreviousStatus = "Pending",
                NewStatus = "Cancelled",
                CustomerEmail = evt.CustomerEmail
            }));

        await eventStore.AppendAsync(order, ct);
        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(PaymentProcessedIntegrationEvent), ct);

        return PaymentResultOutcome.Processed;
    }

    internal enum PaymentResultOutcome { Processed, AlreadyProcessed, OrderNotFound }

    private Task OnError(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Service Bus processor error. Source: {Source}, Entity: {Entity}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
            _processor = null;
        }
        await base.StopAsync(cancellationToken);
    }
}
