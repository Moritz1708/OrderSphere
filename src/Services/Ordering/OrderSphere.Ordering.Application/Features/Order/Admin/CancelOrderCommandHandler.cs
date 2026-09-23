using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.Errors;

namespace OrderSphere.Ordering.Application.Features.Order.Admin;

public sealed record CancelOrderCommand(Guid OrderId) : ICommand<Result>;

public sealed class CancelOrderCommandHandler(
    IOrderingDbContext context,
    IOrderEventStore eventStore,
    ICatalogClient catalogClient,
    ILogger<CancelOrderCommandHandler> logger
) : ICommandHandler<CancelOrderCommand, Result>
{
    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await eventStore.LoadAsync(OrderId.From(request.OrderId), cancellationToken);

            if (order is null)
                return Result.Failure(OrderErrors.OrderNotFoundError);

            // Status before cancellation decides the stock compensation:
            //   Created  → the reservation is still an active hold; release it.
            //   Paid/Shipped → the reservation was confirmed (on-hand stock decremented); restore it.
            var wasConfirmed = order.Status is not OrderStatus.Created;

            var cancel = order.Cancel();
            if (cancel.IsFailure)
            {
                logger.LogWarning("Cannot cancel order {OrderId} in status {Status}", request.OrderId, order.Status);
                return cancel;
            }

            if (wasConfirmed)
            {
                foreach (var item in order.Items)
                {
                    var restoreResult = await catalogClient.RestoreStockAsync(item.ProductId.Value, item.Quantity, cancellationToken);
                    if (restoreResult.IsFailure)
                        logger.LogWarning("Stock restore failed for product {ProductId} during order cancellation", item.ProductId);
                }
            }
            else
            {
                var releaseResult = await catalogClient.ReleaseReservationAsync(order.CorrelationId, cancellationToken);
                if (releaseResult.IsFailure)
                    logger.LogWarning("Reservation release failed for order {OrderId} during cancellation; TTL sweeper will reclaim it.", order.Id);
            }

            await eventStore.AppendAsync(order, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Order {OrderId} cancelled. Stock compensation: {Mode}.",
                order.Id, wasConfirmed ? "restore" : "release");

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel order {OrderId}", request.OrderId);
            return Result.Failure(OrderErrors.UnknownError);
        }
    }
}
