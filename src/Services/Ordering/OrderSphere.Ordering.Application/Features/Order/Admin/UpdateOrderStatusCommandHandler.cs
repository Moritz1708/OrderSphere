using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Abstraction;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Domain.Errors;

namespace OrderSphere.Ordering.Application.Features.Order.Admin;

public sealed record UpdateOrderStatusCommand(Guid OrderId, OrderStatus NewStatus) : ICommand<Result>;

public sealed class UpdateOrderStatusCommandHandler(
    IOrderingDbContext context,
    IOrderEventStore eventStore,
    ILogger<UpdateOrderStatusCommandHandler> logger
) : ICommandHandler<UpdateOrderStatusCommand, Result>
{
    public async Task<Result> Handle(UpdateOrderStatusCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var order = await eventStore.LoadAsync(OrderId.From(request.OrderId), cancellationToken);

            if (order is null)
                return Result.Failure(OrderErrors.OrderNotFoundError);

            var transition = request.NewStatus switch
            {
                OrderStatus.Shipped => order.MarkShipped(),
                OrderStatus.Delivered => order.MarkDelivered(),
                _ => Result.Failure(OrderErrors.InvalidStatusTransition)
            };

            if (transition.IsFailure)
            {
                logger.LogWarning("Invalid status transition for order {OrderId} from {Status} to {NewStatus}",
                    request.OrderId, order.Status, request.NewStatus);
                return transition;
            }

            await eventStore.AppendAsync(order, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Order {OrderId} status updated to {NewStatus}", order.Id, request.NewStatus);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update status for order {OrderId}", request.OrderId);
            return Result.Failure(OrderErrors.UnknownError);
        }
    }
}
