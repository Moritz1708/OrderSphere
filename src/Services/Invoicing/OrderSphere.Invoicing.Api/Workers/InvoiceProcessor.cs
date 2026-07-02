using Azure.Messaging.ServiceBus;
using MediatR;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Invoicing.Application.Features.Invoice.GenerateInvoice;
using AppItemDto = OrderSphere.Invoicing.Application.Models.InvoiceItemDto;
using ContractItemDto = OrderSphere.BuildingBlocks.Contracts.Events.InvoiceItemDto;

namespace OrderSphere.Invoicing.Api.Workers;

public sealed class InvoiceProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<InvoiceProcessor> logger) : BackgroundService
{
    private const string InputQueue = "invoice-generation";
    private const string OutputQueue = "invoice-ready";
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(InputQueue, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 2,
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += OnMessageReceived;
        _processor.ProcessErrorAsync += OnError;

        await _processor.StartProcessingAsync(stoppingToken);
        logger.LogInformation("InvoiceProcessor started, listening on queue '{Queue}'.", InputQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.LogInformation("InvoiceProcessor stopped.");
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var activity = EventBusDiagnostics.StartProcess(args.Message, InputQueue);
        var messageId = args.Message.MessageId;
        logger.LogInformation("Received invoice-generation message {MessageId}.", messageId);

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<OrderPlacedIntegrationEvent>();
            if (evt is null)
            {
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid OrderPlacedIntegrationEvent.");
                return;
            }

            using var tenantScope = AmbientTenantContext.BeginScope(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

            if (await inboxStore.HasBeenProcessedAsync(evt.Id, args.CancellationToken))
            {
                logger.LogInformation("Duplicate invoice-generation event {EventId} — skipping.", evt.Id);
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var command = new GenerateInvoiceCommand(
                evt.OrderId,
                evt.CustomerEmail,
                evt.CustomerName,
                evt.Total,
                evt.Items.Select(i => new AppItemDto(i.ProductName, i.Quantity, i.Price)).ToList());

            var result = await sender.Send(command, args.CancellationToken);

            if (result.IsFailure)
            {
                logger.LogError("Invoice generation failed for order {OrderId}: {Error}.", evt.OrderId, result.Error);
                await args.AbandonMessageAsync(args.Message);
                return;
            }

            var invoiceEvt = new InvoiceGeneratedIntegrationEvent
            {
                OrderId = evt.OrderId,
                InvoiceNumber = result.Value.InvoiceNumber,
                CustomerEmail = evt.CustomerEmail,
                CustomerName = evt.CustomerName,
                Total = evt.Total,
                PdfUrl = result.Value.PdfUrl,
                Items = evt.Items.Select(i => new ContractItemDto(i.ProductName, i.Quantity, i.Price)).ToList(),
            };

            await eventBus.PublishAsync(invoiceEvt, OutputQueue, args.CancellationToken);
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(OrderPlacedIntegrationEvent), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);

            logger.LogInformation("Invoice {InvoiceNumber} generated for order {OrderId}.",
                result.Value.InvoiceNumber, evt.OrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing invoice-generation message {MessageId}. Abandoning.", messageId);
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
