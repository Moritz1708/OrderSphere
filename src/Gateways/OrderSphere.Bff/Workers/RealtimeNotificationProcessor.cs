using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using OrderSphere.Bff.Hubs;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;

namespace OrderSphere.Bff.Workers;

public sealed class RealtimeNotificationProcessor(
    ServiceBusClient serviceBusClient,
    IHubContext<NotificationHub> hubContext,
    ILogger<RealtimeNotificationProcessor> logger) : BackgroundService
{
    private const string QueueName = "realtime-notifications";
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 4,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += OnMessageReceived;
        _processor.ProcessErrorAsync += OnError;

        await _processor.StartProcessingAsync(stoppingToken);
        logger.ProcessorStarted(nameof(RealtimeNotificationProcessor), QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.ProcessorStopped(nameof(RealtimeNotificationProcessor));
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        logger.MessageReceived();

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<RealtimeNotificationEvent>();
            if (evt is null)
            {
                logger.MessageUndeserializable();
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid RealtimeNotificationEvent.");
                return;
            }

            messageScope.SetTenant(evt.TenantId);

            await hubContext.Clients.Group(evt.UserId).SendAsync(
                "ReceiveNotification",
                new
                {
                    evt.Type,
                    evt.Title,
                    evt.Message,
                    evt.OrderId,
                    evt.CreatedAt
                },
                args.CancellationToken);

            logger.LogInformation(
                "Pushed {Type} notification to user {UserId}.",
                evt.Type, evt.UserId);

            await args.CompleteMessageAsync(args.Message);
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
