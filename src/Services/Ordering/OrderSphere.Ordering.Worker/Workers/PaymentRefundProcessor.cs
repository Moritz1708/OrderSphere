using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Domain.Enums;
using OrderSphere.Ordering.Infrastructure.Persistence;

namespace OrderSphere.Ordering.Worker.Workers;

/// <summary>
/// Closes the compensation loop: consumes <see cref="PaymentRefundedIntegrationEvent"/> raised by
/// Payment after a refund and advances the saga to its terminal <see cref="SagaState.Refunded"/>
/// state. The order is already Cancelled by the confirmation-failure compensation; this hop exists
/// purely to make the saga observably terminal.
/// </summary>
public sealed class PaymentRefundProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<PaymentRefundProcessor> logger) : BackgroundService
{
    private const string QueueName = "payment-refunds";
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
        logger.LogInformation("PaymentRefundProcessor started, listening on queue '{Queue}'.", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.LogInformation("PaymentRefundProcessor stopped.");
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var activity = EventBusDiagnostics.StartProcess(args.Message, QueueName);
        var messageId = args.Message.MessageId;
        logger.LogInformation("Received payment refund message {MessageId}", messageId);

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<PaymentRefundedIntegrationEvent>();
            if (evt is null)
            {
                logger.LogError("Message {MessageId} could not be deserialized. Dead-lettering.", messageId);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid PaymentRefundedIntegrationEvent.");
                return;
            }

            using var tenantScope = AmbientTenantContext.BeginScope(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

            await ProcessRefundAsync(evt, context, inboxStore, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing payment refund {MessageId}. Abandoning.", messageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    internal async Task ProcessRefundAsync(
        PaymentRefundedIntegrationEvent evt,
        OrderingDbContext context,
        IInboxStore inboxStore,
        CancellationToken ct)
    {
        if (await inboxStore.HasBeenProcessedAsync(evt.Id, ct))
        {
            logger.LogInformation("Event {EventId} already processed.", evt.Id);
            return;
        }

        // Returns/RMA flow: the refund settles an approved return request rather than a failed-
        // confirmation compensation, so advance the return — not the saga — to its terminal state.
        if (evt.ReturnRequestId is { } returnRequestId)
        {
            await ProcessReturnRefundAsync(returnRequestId, evt, context, inboxStore, ct);
            return;
        }

        var saga = await context.OrderSagas.FirstOrDefaultAsync(s => s.CorrelationId == evt.CorrelationId, ct);
        if (saga is null)
        {
            logger.LogWarning("No saga found for correlation {CorrelationId} on refund; marking processed.",
                evt.CorrelationId);
        }
        else
        {
            saga.MarkRefunded();
            OrderingMetrics.RecordSagaTransition(nameof(SagaState.Refunded));
            logger.LogInformation("Saga {CorrelationId} advanced to Refunded after payment refund for order {OrderId}.",
                evt.CorrelationId, evt.OrderId);
        }

        // Single SaveChanges commits the saga transition and the inbox mark atomically.
        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(PaymentRefundedIntegrationEvent), ct);
    }

    private async Task ProcessReturnRefundAsync(
        Guid returnRequestId,
        PaymentRefundedIntegrationEvent evt,
        OrderingDbContext context,
        IInboxStore inboxStore,
        CancellationToken ct)
    {
        var returnRequest = await context.ReturnRequests
            .FirstOrDefaultAsync(r => r.Id == ReturnRequestId.From(returnRequestId), ct);

        if (returnRequest is null)
        {
            logger.LogWarning("No return request {ReturnRequestId} found on refund; marking processed.",
                returnRequestId);
        }
        else
        {
            var transition = returnRequest.MarkRefunded();
            if (transition.IsFailure)
                logger.LogWarning("Return {ReturnRequestId} could not be marked refunded ({Status}): {Error}.",
                    returnRequestId, returnRequest.Status, transition.Error.Code);
            else
                logger.LogInformation("Return {ReturnRequestId} advanced to Refunded for order {OrderId}.",
                    returnRequestId, evt.OrderId);
        }

        // Single SaveChanges commits the return transition and the inbox mark atomically.
        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(PaymentRefundedIntegrationEvent), ct);
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
