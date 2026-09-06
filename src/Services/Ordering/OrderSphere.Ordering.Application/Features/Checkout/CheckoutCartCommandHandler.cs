using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Errors;
using OrderSphere.Ordering.Domain.Events;

namespace OrderSphere.Ordering.Application.Features.Checkout;

public sealed class CheckoutCartCommandHandler(
    ICatalogClient catalogClient,
    IBasketClient basketClient,
    IOrderingServiceBusPublisher serviceBusPublisher,
    ICheckoutIdempotencyStore idempotencyStore,
    ILogger<CheckoutCartCommandHandler> logger
) : ICommandHandler<CheckoutCartCommand, Result<Guid>>
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromMinutes(30);

    public async Task<Result<Guid>> Handle(CheckoutCartCommand request, CancellationToken cancellationToken)
    {
        // Idempotency guard: return the stored result for a key we have already processed.
        // Distributed (Redis) so the guard holds across service instances.
        var cacheKey = $"checkout:{request.CustomerId}:{request.IdempotencyKey}";
        var cachedCorrelationId = await idempotencyStore.TryGetCorrelationIdAsync(cacheKey, cancellationToken);
        if (cachedCorrelationId is { } existingCorrelationId)
        {
            logger.LogInformation(
                "Duplicate checkout request for customer {CustomerId}. Returning cached CorrelationId: {CorrelationId}",
                request.CustomerId, existingCorrelationId);
            return Result<Guid>.Success(existingCorrelationId);
        }

        // CorrelationId is derived deterministically so retries produce the same value, letting
        // the reservation and the Worker's uniqueness check deduplicate repeated checkouts.
        var correlationId = DeriveCorrelationId(request.CustomerId, request.IdempotencyKey);

        // Set once the checkout is accepted (event published). Until then, a reservation taken
        // below is compensated (released) in the finally block.
        var reserved = false;
        var committed = false;

        try
        {
            // 1. Fetch cart. ICatalogClient still uses Guid — convert at service boundary.
            var cartResult = await basketClient.GetCartAsync(request.CustomerId.Value, cancellationToken);
            if (cartResult.IsFailure)
            {
                // An empty or expired cart is a normal user outcome, returned as a Result
                // failure — not an error condition for the service.
                logger.LogWarning("Cart not found for customer {CustomerId} via Basket service", request.CustomerId);
                return Result<Guid>.Failure(CheckoutCartErrors.CartNotFoundError);
            }

            var cart = cartResult.Value;
            if (cart.Items.Count == 0)
            {
                logger.LogWarning("Checkout attempted on empty cart for customer {CustomerId}", request.CustomerId);
                return Result<Guid>.Failure(CheckoutCartErrors.EmptyCartError);
            }

            // 2. Resolve all product details — read-only, no state change yet.
            var orderItemDtos = new List<OrderItemEventDto>(cart.Items.Count);
            foreach (var cartItem in cart.Items)
            {
                var productResult = await catalogClient.GetProductByIdAsync(cartItem.ProductId, cancellationToken);
                if (productResult.IsFailure)
                {
                    logger.LogWarning("Product {ProductId} not found in Catalog during checkout for customer {CustomerId}",
                        cartItem.ProductId, request.CustomerId);
                    return Result<Guid>.Failure(ProductErrors.ProductNotFoundError);
                }

                orderItemDtos.Add(new OrderItemEventDto(
                    cartItem.ProductId,
                    productResult.Value.Name,
                    cartItem.Quantity,
                    productResult.Value.Price,
                    productResult.Value.CategoryId == default ? null : productResult.Value.CategoryId));
            }

            // 3. Reserve stock against the correlation id. The hold is confirmed (decremented)
            // on payment success or released on failure/cancellation/TTL — no stock is committed
            // before payment. A single call reserves all items atomically.
            var reserveResult = await catalogClient.ReserveStockAsync(
                correlationId,
                orderItemDtos.Select(i => new ReservationItem(i.ProductId, i.Quantity)).ToList(),
                cancellationToken);

            if (reserveResult.IsFailure)
            {
                logger.LogWarning("Stock reservation failed for customer {CustomerId}: {Error}",
                    request.CustomerId, reserveResult.Error.Code);
                return Result<Guid>.Failure(ProductErrors.InsufficientStockError);
            }

            reserved = true;

            // 4. Publish to Service Bus — if this throws, finally releases the reservation.
            var checkoutCartEvent = new CheckoutCartEvent(
                correlationId,
                new CheckoutCartDto(
                    request.CustomerId.Value,
                    request.CustomerEmail,
                    request.CustomerName,
                    request.ShippingAddress,
                    request.PaymentMethod,
                    request.CouponCode),
                orderItemDtos);

            await serviceBusPublisher.PublishCheckoutCartEventAsync(checkoutCartEvent, cancellationToken);

            await idempotencyStore.SetCorrelationIdAsync(cacheKey, correlationId, IdempotencyTtl, cancellationToken);
            committed = true;

            // 5. Clear cart. Best-effort: the order is already committed to the bus.
            var clearResult = await basketClient.ClearCartItemsAsync(request.CustomerId.Value, cancellationToken);
            if (clearResult.IsFailure)
            {
                logger.LogWarning(
                    "Cart clear failed for customer {CustomerId} after accepted checkout. CorrelationId: {CorrelationId}. Cart may appear stale.",
                    request.CustomerId, correlationId);
            }

            logger.LogInformation(
                "Checkout accepted for customer {CustomerId}. CorrelationId: {CorrelationId}",
                request.CustomerId, correlationId);

            return Result<Guid>.Success(correlationId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checkout failed for customer {CustomerId}", request.CustomerId);
            return Result<Guid>.Failure(CheckoutCartErrors.UnknownError);
        }
        finally
        {
            // Release the hold if it was taken but the checkout was not accepted.
            // (If never released here, the Catalog TTL sweeper releases it after expiry.)
            if (reserved && !committed)
            {
                logger.LogWarning(
                    "Releasing stock reservation after checkout failure for customer {CustomerId}. CorrelationId: {CorrelationId}",
                    request.CustomerId, correlationId);

                var releaseResult = await catalogClient.ReleaseReservationAsync(correlationId, CancellationToken.None);
                if (releaseResult.IsFailure)
                {
                    logger.LogError(
                        "COMPENSATION: immediate reservation release failed for CorrelationId {CorrelationId}; TTL sweeper will reclaim it.",
                        correlationId);
                }
            }
        }
    }

    private static Guid DeriveCorrelationId(CustomerId customerId, Guid idempotencyKey)
    {
        var input = Encoding.UTF8.GetBytes($"{customerId.Value}:{idempotencyKey}");
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan()[..16]);
    }
}
