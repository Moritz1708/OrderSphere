using Azure.Messaging.ServiceBus;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Ordering.Domain.Entities;
using OrderSphere.Ordering.Infrastructure.Persistence;

namespace OrderSphere.Ordering.Worker.Workers;

/// <summary>
/// Projects <see cref="OrderStatusChangedIntegrationEvent"/> into the <c>order_history</c>
/// CQRS read-model. Consumes the dedicated <c>order-history</c> queue (fanned out from the
/// outbox alongside <c>webhook-events</c>) so the read-model is maintained asynchronously,
/// decoupled from the order write transaction. Idempotent via the inbox: a redelivered event
/// inserts no duplicate row.
/// </summary>
public sealed class OrderHistoryProjector(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderHistoryProjector> logger) : BackgroundService
{
    private const string QueueName = "order-history";

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
        logger.LogInformation("OrderHistoryProjector started, listening on queue '{Queue}'.", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.LogInformation("OrderHistoryProjector stopped.");
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var activity = EventBusDiagnostics.StartProcess(args.Message, QueueName);
        var messageId = args.Message.MessageId;
        logger.LogInformation("Received order-history message {MessageId}", messageId);

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<OrderStatusChangedIntegrationEvent>();
            if (evt is null)
            {
                logger.LogError("Message {MessageId} could not be deserialized. Dead-lettering.", messageId);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid OrderStatusChangedIntegrationEvent.");
                return;
            }

            using var tenantScope = AmbientTenantContext.BeginScope(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

            await ProjectAsync(evt, context, inboxStore, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception projecting order-history {MessageId}. Abandoning.", messageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    /// <summary>
    /// Appends one read-model row for the status transition, unless the event was already
    /// processed. The row insert and the inbox mark commit atomically in the single
    /// SaveChanges inside <see cref="IInboxStore.MarkAsProcessedAsync"/>.
    /// </summary>
    internal async Task ProjectAsync(
        OrderStatusChangedIntegrationEvent evt,
        OrderingDbContext context,
        IInboxStore inboxStore,
        CancellationToken ct)
    {
        if (await inboxStore.HasBeenProcessedAsync(evt.Id, ct))
        {
            logger.LogInformation("Event {EventId} already projected.", evt.Id);
            return;
        }

        context.OrderHistory.Add(OrderHistoryEntry.Record(
            evt.OrderId,
            evt.CorrelationId,
            evt.CustomerEmail,
            evt.PreviousStatus,
            evt.NewStatus,
            evt.CreatedAt));

        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(OrderStatusChangedIntegrationEvent), ct);

        logger.LogInformation(
            "Projected order {OrderId} transition {Previous} -> {New} into order_history.",
            evt.OrderId, evt.PreviousStatus, evt.NewStatus);
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
}
