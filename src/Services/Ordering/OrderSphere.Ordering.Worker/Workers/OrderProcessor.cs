using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.BuildingBlocks.ValueObjects;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.ValueObjects;
using OrderSphere.Ordering.Infrastructure.EventSourcing;
using OrderSphere.Ordering.Infrastructure.Persistence;

namespace OrderSphere.Ordering.Worker.Workers;

public sealed class OrderProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    IShippingRateProvider shippingRateProvider,
    ILogger<OrderProcessor> logger) : BackgroundService
{
    private const string QueueName = "orders";
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
        logger.LogInformation("OrderProcessor started, listening on queue '{Queue}'.", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.LogInformation("OrderProcessor stopped.");
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var activity = EventBusDiagnostics.StartProcess(args.Message, QueueName);
        var messageId = args.Message.MessageId;
        logger.LogInformation("Received message {MessageId}", messageId);

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<CheckoutCartIntegrationEvent>();
            if (evt is null)
            {
                logger.LogError("Message {MessageId} could not be deserialized. Dead-lettering.", messageId);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid CheckoutCartEvent.");
                return;
            }

            using var tenantScope = AmbientTenantContext.BeginScope(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

            var result = await ProcessOrderAsync(evt, context, args.CancellationToken);

            if (result.IsSuccess)
            {
                await args.CompleteMessageAsync(args.Message);
                logger.LogInformation("Message {MessageId} processed. CorrelationId: {CorrelationId}",
                    messageId, evt.CorrelationId);
            }
            else
            {
                logger.LogWarning("ProcessOrder returned failure for message {MessageId}: {Error}. Abandoning.",
                    messageId, result.ErrorMessage);
                await args.AbandonMessageAsync(args.Message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing message {MessageId}. Abandoning.", messageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    internal async Task<ProcessResult> ProcessOrderAsync(
        CheckoutCartIntegrationEvent evt,
        OrderingDbContext context,
        CancellationToken ct)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                // Idempotency check — re-evaluated on each retry attempt.
                var existingOrderId = await context.Orders
                    .Where(o => o.CorrelationId == evt.CorrelationId)
                    .Select(o => (OrderId?)o.Id)
                    .FirstOrDefaultAsync(ct);

                if (existingOrderId is not null)
                {
                    logger.LogInformation("Duplicate message for CorrelationId {CorrelationId} ignored. Existing OrderId: {OrderId}",
                        evt.CorrelationId, existingOrderId);
                    return ProcessResult.Ok();
                }

                await context.BeginTransactionAsync(ct);

                try
                {
                    var orderItems = evt.Items
                        .Select(i => new OrderItem(ProductId.From(i.ProductId), i.ProductName, Quantity.Of(i.Quantity), Money.Of(i.Price), i.CategoryId))
                        .ToList();

                    var addr = evt.ShippingAddress;
                    var shippingAddress = new Address(addr.FirstName, addr.LastName, addr.Street, addr.City, addr.PostalCode, addr.Country);
                    var paymentMethod = Enum.Parse<PaymentMethod>(evt.PaymentMethod);

                    var order = Order.Create(
                        CustomerId.From(evt.CustomerId),
                        shippingAddress,
                        paymentMethod,
                        orderItems,
                        evt.CorrelationId);

                    // Apply a coupon (if carried through checkout) within this transaction.
                    // The discount is computed server-side — the client only sends the code.
                    var subtotal = evt.Items.Sum(i => i.Price * i.Quantity);
                    var itemLines = evt.Items.Select(i => (i.CategoryId, (decimal)(i.Price * i.Quantity))).ToList();
                    var discount = await ApplyCouponAsync(evt.CouponCode, subtotal, itemLines, order, context, ct);

                    var shipping = shippingRateProvider.Calculate(subtotal);
                    order.SetShippingCost(shipping);

                    // Persist the aggregate as its event stream and stage the read projection.
                    // The CorrelationId unique index on the projection still guards duplicates.
                    await new OrderEventStore(context).AppendAsync(order, ct);

                    var paymentEvent = new PaymentRequestedIntegrationEvent
                    {
                        CorrelationId = evt.CorrelationId,
                        OrderId = order.Id.Value,
                        Amount = subtotal - discount + shipping,
                        Currency = "EUR",
                        PaymentMethod = evt.PaymentMethod,
                        CustomerEmail = evt.CustomerEmail
                    };

                    context.AddOutboxMessage(
                        nameof(PaymentRequestedIntegrationEvent),
                        JsonSerializer.Serialize(paymentEvent));

                    // Saga read-model: order created + payment requested are committed together,
                    // so the saga starts at PaymentRequested. Written in the same transaction.
                    var saga = OrderSaga.Start(evt.CorrelationId, order.Id.Value);
                    saga.MarkPaymentRequested();
                    await context.OrderSagas.AddAsync(saga, ct);

                    await context.CommitAsync(ct);

                    logger.LogInformation(
                        "Order {OrderId} created for customer {CustomerId}. CorrelationId: {CorrelationId}",
                        order.Id, order.CustomerId, evt.CorrelationId);

                    OrderingMetrics.OrdersPlaced.Add(1);
                    OrderingMetrics.RecordSagaTransition(nameof(SagaState.PaymentRequested));

                    return ProcessResult.Ok();
                }
                catch (DbUpdateException dbEx) when (IsUniqueConstraintViolation(dbEx))
                {
                    await context.RollbackAsync(ct);
                    logger.LogWarning(dbEx, "Concurrent insert detected for CorrelationId {CorrelationId}.",
                        evt.CorrelationId);
                    return ProcessResult.Ok(); // idempotent — another instance won the race
                }
                catch
                {
                    await context.RollbackAsync(ct);
                    throw; // let the execution strategy retry on transient failures
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process order for customer {CustomerId}. CorrelationId: {CorrelationId}",
                evt.CustomerId, evt.CorrelationId);
            return ProcessResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Re-validates and redeems a coupon within the order-creation transaction, sets the discount
    /// on the order, and returns the discount amount. A coupon that has become invalid between
    /// checkout and processing (expired, usage limit) is ignored — checkout is not failed for it.
    /// Category-scoped coupons compute the discount on the filtered item subtotal only.
    /// </summary>
    private async Task<decimal> ApplyCouponAsync(
        string? couponCode,
        decimal subtotal,
        IReadOnlyList<(Guid? CategoryId, decimal LineTotal)> itemLines,
        Order order,
        OrderingDbContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
            return 0m;

        var normalized = Coupon.Normalize(couponCode);
        var coupon = await context.Coupons
            .AsTracking()
            .FirstOrDefaultAsync(c => c.Code == normalized, ct);

        if (coupon is null)
        {
            logger.LogWarning("Coupon {Code} not found during order processing; ignoring.", normalized);
            return 0m;
        }

        var effectiveSubtotal = coupon.ComputeScopedSubtotal(itemLines);
        var discountResult = coupon.CalculateDiscount(effectiveSubtotal, DateTime.UtcNow);
        if (discountResult.IsFailure)
        {
            logger.LogWarning("Coupon {Code} not applicable ({Reason}); ignoring.", normalized, discountResult.Error.Code);
            return 0m;
        }

        var redeem = coupon.Redeem();
        if (redeem.IsFailure)
        {
            logger.LogWarning("Coupon {Code} could not be redeemed ({Reason}); ignoring.", normalized, redeem.Error.Code);
            return 0m;
        }

        order.ApplyDiscount(normalized, discountResult.Value);
        logger.LogInformation("Applied coupon {Code} to order {CorrelationId}: -{Discount}",
            normalized, order.CorrelationId, discountResult.Value);
        return discountResult.Value;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        return inner is not null
            && (inner.Message.Contains("23505", StringComparison.Ordinal)
                || (inner.GetType().Name.Contains("PostgresException", StringComparison.Ordinal)
                    && inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)));
    }

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

    internal sealed record ProcessResult(bool IsSuccess, string? ErrorMessage)
    {
        public static ProcessResult Ok() => new(true, null);
        public static ProcessResult Fail(string error) => new(false, error);
    }
}
