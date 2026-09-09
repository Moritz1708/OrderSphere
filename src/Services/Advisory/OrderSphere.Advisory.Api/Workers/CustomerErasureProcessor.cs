using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.Advisory.Infrastructure.Persistence;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;

namespace OrderSphere.Advisory.Api.Workers;

/// <summary>
/// D1 — GDPR right-to-erasure. Consumes UserProfile's fan-out queue and hard-deletes every
/// conversation (and, via cascade, its messages) for this customer. Unlike the other consumers,
/// conversation transcripts are free text that cannot be selectively anonymized, so the row is
/// removed outright rather than PII fields being overwritten.
/// </summary>
public sealed class CustomerErasureProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<CustomerErasureProcessor> logger) : BackgroundService
{
    private const string QueueName = "erasure-advisory";
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
            var context = scope.ServiceProvider.GetRequiredService<AdvisoryDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

            if (await inboxStore.HasBeenProcessedAsync(evt.Id, args.CancellationToken))
            {
                logger.DuplicateMessageIgnored();
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var conversations = await context.Conversations
                .IgnoreQueryFilters() // erase soft-deleted conversations too
                .Where(c => c.CustomerSub == evt.CustomerSub)
                .ToListAsync(args.CancellationToken);

            context.Conversations.RemoveRange(conversations);

            await context.SaveChangesAsync(args.CancellationToken);
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(CustomerErasureRequestedIntegrationEvent), args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);

            logger.LogInformation("Hard-deleted {Count} conversation(s) for erased customer.", conversations.Count);
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
