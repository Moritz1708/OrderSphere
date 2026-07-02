using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Infrastructure.Providers;

namespace OrderSphere.Payment.Worker.Workers;

/// <summary>
/// Consumes <see cref="RefundRequestedIntegrationEvent"/> raised by Ordering when a return/RMA is
/// approved. Refunds the captured payment through the originating provider (or simulates it under
/// provider bypass), marks the record refunded, and publishes <see cref="PaymentRefundedIntegrationEvent"/>
/// carrying the originating <c>ReturnRequestId</c> so Ordering can settle the return.
/// </summary>
public sealed class RefundRequestedProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentOptions> options,
    ILogger<RefundRequestedProcessor> logger) : BackgroundService
{
    private const string QueueName = "refund-requested";
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
        logger.LogInformation("RefundRequestedProcessor started, listening on queue '{Queue}'.", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.LogInformation("RefundRequestedProcessor stopped.");
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var activity = EventBusDiagnostics.StartProcess(args.Message, QueueName);
        var messageId = args.Message.MessageId;
        logger.LogInformation("Received refund-requested message {MessageId}", messageId);

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<RefundRequestedIntegrationEvent>();
            if (evt is null)
            {
                logger.LogError("Message {MessageId} could not be deserialized. Dead-lettering.", messageId);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid RefundRequestedIntegrationEvent.");
                return;
            }

            using var tenantScope = AmbientTenantContext.BeginScope(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var providerFactory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

            await ProcessRefundRequestAsync(evt, context, inboxStore, providerFactory, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing refund-requested {MessageId}. Abandoning.", messageId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    internal async Task ProcessRefundRequestAsync(
        RefundRequestedIntegrationEvent evt,
        PaymentDbContext context,
        IInboxStore inboxStore,
        IPaymentProviderFactory providerFactory,
        CancellationToken ct)
    {
        if (await inboxStore.HasBeenProcessedAsync(evt.Id, ct))
        {
            logger.LogInformation("Event {EventId} already processed.", evt.Id);
            return;
        }

        var payment = await context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == OrderId.From(evt.OrderId), ct);

        if (payment is null)
        {
            // No capture on record — nothing to refund. Mark processed to avoid endless retry.
            logger.LogError("No payment found for order {OrderId} on refund request {ReturnRequestId}; cannot refund.",
                evt.OrderId, evt.ReturnRequestId);
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(RefundRequestedIntegrationEvent), ct);
            return;
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            logger.LogWarning("Payment for order {OrderId} already refunded; closing return refund loop.", evt.OrderId);
        }
        else if (payment.Status != PaymentStatus.Captured)
        {
            logger.LogWarning("Payment for order {OrderId} is {Status}; skipping provider refund.",
                evt.OrderId, payment.Status);
        }
        else if (options.Value.BypassProviders)
        {
            payment.MarkRefunded();
            logger.LogInformation("Provider bypass active — marking order {OrderId} refunded without contacting a provider.",
                evt.OrderId);
        }
        else
        {
            var provider = providerFactory.GetProvider(payment.PaymentMethod);
            if (provider is null)
                throw new InvalidOperationException(
                    $"No provider for method '{payment.PaymentMethod}' to refund order {evt.OrderId}.");

            var refund = await provider.RefundAsync(payment.TransactionId!, evt.Amount, ct);
            if (refund.IsFailure)
                // Throw so Service Bus redelivers — a transient refund failure must not silently drop.
                throw new InvalidOperationException(
                    $"Refund failed for order {evt.OrderId}: {refund.Error.Description ?? refund.Error.Code}");

            payment.MarkRefunded();
            logger.LogInformation("Refunded payment for order {OrderId} (return {ReturnRequestId}). TransactionId: {TransactionId}",
                evt.OrderId, evt.ReturnRequestId, payment.TransactionId);
        }

        context.AddOutboxMessage(
            nameof(PaymentRefundedIntegrationEvent),
            JsonSerializer.Serialize(new PaymentRefundedIntegrationEvent
            {
                CorrelationId = evt.CorrelationId,
                OrderId = evt.OrderId,
                TransactionId = payment.TransactionId,
                Reason = evt.Reason,
                ReturnRequestId = evt.ReturnRequestId
            }));

        // Single SaveChanges commits the refunded state, the outbox row, and the inbox mark atomically.
        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(RefundRequestedIntegrationEvent), ct);
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
