using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Ordering.Infrastructure.Persistence;

namespace OrderSphere.Ordering.Worker.Workers;

/// <summary>
/// D1 — GDPR right-to-erasure. Consumes UserProfile's fan-out queue and anonymizes the shipping
/// address on every order the customer placed. Order rows are kept (financial retention); see
/// <see cref="OrderSphere.Ordering.Domain.ReadModels.OrderView.AnonymizeShippingAddress"/>.
/// </summary>
public sealed class CustomerErasureProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<CustomerErasureProcessor> logger) : BackgroundService
{
    private const string QueueName = "erasure-ordering";
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += OnMessageReceived;
        _processor.ProcessErrorAsync += OnError;

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("CustomerErasureProcessor started, listening on queue '{Queue}'.", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.LogInformation("CustomerErasureProcessor stopped.");
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var activity = EventBusDiagnostics.StartProcess(args.Message, QueueName);
        var messageId = args.Message.MessageId;
        logger.LogInformation("Received erasure-ordering message {MessageId}.", messageId);

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<CustomerErasureRequestedIntegrationEvent>();
            if (evt is null)
            {
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid CustomerErasureRequestedIntegrationEvent.");
                return;
            }

            using var tenantScope = AmbientTenantContext.BeginScope(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

            if (await inboxStore.HasBeenProcessedAsync(evt.Id, args.CancellationToken))
            {
                logger.LogInformation("Duplicate erasure-ordering event {EventId} — skipping.", evt.Id);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var customerId = CustomerId.FromSub(evt.CustomerSub);
            var orders = await context.Orders
                .Where(o => o.CustomerId == customerId)
                .ToListAsync(args.CancellationToken);

            foreach (var order in orders)
                order.AnonymizeShippingAddress();

            // MarkAsProcessedAsync issues the single SaveChangesAsync that commits the
            // anonymized orders and the inbox row atomically.
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(CustomerErasureRequestedIntegrationEvent), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);

            logger.LogInformation("Anonymized {Count} order(s) for erased customer.", orders.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing erasure-ordering message {MessageId}. Abandoning.", messageId);
            await args.AbandonMessageAsync(args.Message);
        }
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
