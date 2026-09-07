using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.Payment.Infrastructure.Persistence;

namespace OrderSphere.Payment.Worker.Workers;

/// <summary>
/// D1 — GDPR right-to-erasure. Consumes UserProfile's fan-out queue and anonymizes the customer
/// email on every payment record for this customer. Records are kept (financial retention); see
/// <see cref="OrderSphere.Payment.Domain.Entities.PaymentRecord.AnonymizeCustomerEmail"/>.
/// </summary>
public sealed class CustomerErasureProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<CustomerErasureProcessor> logger) : BackgroundService
{
    private const string QueueName = "erasure-payment";
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
        logger.ProcessorStarted(nameof(CustomerErasureProcessor), QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.ProcessorStopped(nameof(CustomerErasureProcessor));
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        logger.MessageReceived();

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

            messageScope.SetTenant(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

            if (await inboxStore.HasBeenProcessedAsync(evt.Id, args.CancellationToken))
            {
                logger.DuplicateMessageIgnored();
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var payments = await context.Payments
                .Where(p => p.CustomerEmail == evt.CustomerEmail)
                .ToListAsync(args.CancellationToken);

            foreach (var payment in payments)
                payment.AnonymizeCustomerEmail();

            // MarkAsProcessedAsync issues the single SaveChangesAsync that commits the
            // anonymized payments and the inbox row atomically.
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(CustomerErasureRequestedIntegrationEvent), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);

            logger.LogInformation("Anonymized {Count} payment record(s) for erased customer.", payments.Count);
        }
        catch (Exception ex)
        {
            logger.MessageProcessingFailed(ex);
            await args.AbandonMessageAsync(args.Message);
        }
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
