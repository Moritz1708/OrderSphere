using Azure.Messaging.ServiceBus;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.Notification.Worker.Email;

namespace OrderSphere.Notification.Worker.Workers;

public sealed class InvoiceGeneratedProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<InvoiceGeneratedProcessor> logger) : BackgroundService
{
    private const string QueueName = "invoice-ready";
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += OnMessageReceived;
        _processor.ProcessErrorAsync += OnError;

        await _processor.StartProcessingAsync(stoppingToken);
        logger.ProcessorStarted(nameof(InvoiceGeneratedProcessor), QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.ProcessorStopped(nameof(InvoiceGeneratedProcessor));
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        logger.MessageReceived();

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<InvoiceGeneratedIntegrationEvent>();
            if (evt is null)
            {
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid InvoiceGeneratedIntegrationEvent.");
                return;
            }

            messageScope.SetTenant(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var emailService = scope.ServiceProvider.GetRequiredService<INotificationEmailService>();

            if (await inboxStore.HasBeenProcessedAsync(evt.Id, args.CancellationToken))
            {
                logger.DuplicateMessageIgnored();
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            await emailService.SendInvoiceReadyAsync(evt, args.CancellationToken);
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(InvoiceGeneratedIntegrationEvent), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);

            logger.LogInformation("Invoice-ready notification sent for invoice {InvoiceNumber}.", evt.InvoiceNumber);
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
