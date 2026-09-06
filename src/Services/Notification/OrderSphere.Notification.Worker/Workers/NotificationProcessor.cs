using Azure.Messaging.ServiceBus;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.Notification.Worker.Channels;
using OrderSphere.Notification.Worker.Clients;

namespace OrderSphere.Notification.Worker.Workers;

public sealed class NotificationProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationProcessor> logger) : BackgroundService
{
    private const string QueueName = "notification-orders";
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
        logger.ProcessorStarted(nameof(NotificationProcessor), QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.ProcessorStopped(nameof(NotificationProcessor));
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        logger.MessageReceived();

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<OrderPlacedIntegrationEvent>();
            if (evt is null)
            {
                logger.MessageUndeserializable();
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid OrderPlacedIntegrationEvent.");
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var channels = scope.ServiceProvider.GetRequiredService<IEnumerable<INotificationChannel>>();
            var profileClient = scope.ServiceProvider.GetRequiredService<IUserProfileClient>();

            await ProcessNotificationAsync(evt, inboxStore, channels, profileClient, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex)
        {
            logger.MessageProcessingFailed(ex);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    internal async Task ProcessNotificationAsync(
        OrderPlacedIntegrationEvent evt,
        IInboxStore inboxStore,
        IEnumerable<INotificationChannel> channels,
        IUserProfileClient profileClient,
        CancellationToken ct)
    {
        // Idempotency check — guard against ASB at-least-once redelivery.
        if (await inboxStore.HasBeenProcessedAsync(evt.Id, ct))
        {
            logger.LogInformation("Duplicate notification event {EventId} — skipping.", evt.Id);
            return;
        }

        if (string.IsNullOrWhiteSpace(evt.CustomerEmail))
        {
            logger.LogWarning("OrderPlacedEvent {OrderId} has no customer email. Completing without sending.", evt.OrderId);
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(OrderPlacedIntegrationEvent), ct);
            return;
        }

        // Fetch channel preferences; defaults to email-only on failure.
        var prefs = await profileClient.GetNotificationPreferencesAsync(evt.CustomerEmail, ct);

        var channelMap = channels.ToDictionary(c => c.ChannelType);
        var tasks = new List<Task>();

        if (prefs.EmailEnabled && channelMap.TryGetValue(NotificationChannelType.Email, out var email))
            tasks.Add(email.SendOrderConfirmationAsync(evt, ct));
        if (prefs.SmsEnabled && channelMap.TryGetValue(NotificationChannelType.Sms, out var sms))
            tasks.Add(sms.SendOrderConfirmationAsync(evt, ct));
        if (prefs.PushEnabled && channelMap.TryGetValue(NotificationChannelType.Push, out var push))
            tasks.Add(push.SendOrderConfirmationAsync(evt, ct));

        await Task.WhenAll(tasks);
        await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(OrderPlacedIntegrationEvent), ct);

        logger.LogInformation(
            "Notification dispatched for order {OrderId} via {ChannelCount} channel(s).",
            evt.OrderId, tasks.Count);
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
