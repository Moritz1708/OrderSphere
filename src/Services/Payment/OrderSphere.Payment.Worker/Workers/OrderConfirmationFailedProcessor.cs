using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Infrastructure.Providers;

namespace OrderSphere.Payment.Worker.Workers;

/// <summary>
/// Consumes <see cref="OrderConfirmationFailedIntegrationEvent"/> raised by Ordering when a captured
/// payment can no longer be matched to a confirmable order. Refunds the captured payment through the
/// originating provider (or simulates it under provider bypass), marks the record refunded, and
/// publishes <see cref="PaymentRefundedIntegrationEvent"/> so Ordering can close the saga.
/// </summary>
public sealed class OrderConfirmationFailedProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentOptions> options,
    ILogger<OrderConfirmationFailedProcessor> logger) : BackgroundService
{
    private const string QueueName = "order-confirmation-failed";
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
        logger.ProcessorStarted(nameof(OrderConfirmationFailedProcessor), QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.ProcessorStopped(nameof(OrderConfirmationFailedProcessor));
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        logger.MessageReceived();

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<OrderConfirmationFailedIntegrationEvent>();
            if (evt is null)
            {
                logger.MessageUndeserializable();
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid OrderConfirmationFailedIntegrationEvent.");
                return;
            }

            messageScope.SetTenant(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var providerFactory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

            await ProcessConfirmationFailureAsync(evt, context, inboxStore, providerFactory, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.MessageProcessingFailed(ex);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    internal async Task ProcessConfirmationFailureAsync(
        OrderConfirmationFailedIntegrationEvent evt,
        PaymentDbContext context,
        IInboxStore inboxStore,
        IPaymentProviderFactory providerFactory,
        CancellationToken ct)
    {
        if (await inboxStore.HasBeenProcessedAsync(evt.Id, ct))
        {
            logger.DuplicateMessageIgnored();
            return;
        }

        var payment = await context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == OrderId.From(evt.OrderId), ct);

        if (payment is null)
        {
            // No capture to reverse — nothing to refund. Mark processed to avoid endless retry.
            logger.LogWarning("No payment found for order {OrderId} on confirmation failure; nothing to refund.", evt.OrderId);
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(OrderConfirmationFailedIntegrationEvent), ct);
            return;
        }

        if (payment.Status != PaymentStatus.Captured)
        {
            // Only a captured payment needs reversing. Anything else (already refunded, failed) is a
            // no-op for the refund itself, but we still close the loop so the saga can terminate.
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

            var refund = await provider.RefundAsync(payment.TransactionId!, payment.Amount, ct);
            if (refund.IsFailure)
                // Throw so Service Bus redelivers — a transient refund failure must not silently drop.
                throw new InvalidOperationException(
                    $"Refund failed for order {evt.OrderId}: {refund.Error.Description ?? refund.Error.Code}");

            payment.MarkRefunded();
            logger.LogInformation("Refunded payment for order {OrderId}. TransactionId: {TransactionId}",
                evt.OrderId, payment.TransactionId);
        }

        context.AddOutboxMessage(
            nameof(PaymentRefundedIntegrationEvent),
            JsonSerializer.Serialize(new PaymentRefundedIntegrationEvent
            {
                CorrelationId = evt.CorrelationId,
                OrderId = evt.OrderId,
                TransactionId = payment.TransactionId,
                Reason = evt.Reason
            }));

        // Single SaveChanges commits the refunded state, the outbox row, and the inbox mark atomically.
        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(OrderConfirmationFailedIntegrationEvent), ct);
    }

    private Task OnError(ProcessErrorEventArgs args)
    {
        logger.ProcessorError(args.Exception, args.EntityPath, args.ErrorSource.ToString());
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
