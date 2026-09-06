using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.Webhooks.Domain.Entities;
using OrderSphere.Webhooks.Domain.Enums;
using OrderSphere.Webhooks.Infrastructure.Persistence;

namespace OrderSphere.Webhooks.Worker.Workers;

/// <summary>
/// Consumes integration events from the <c>webhook-events</c> Service Bus queue,
/// matches them against active webhook subscriptions, and creates delivery records
/// for each matching subscription.
/// </summary>
public sealed class WebhookEventProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookEventProcessor> logger) : BackgroundService
{
    private const string QueueName = "webhook-events";
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4,
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        // Keep the service alive until shutdown is requested.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await _processor.StopProcessingAsync();
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        var messageId = args.Message.MessageId;
        logger.MessageReceived();

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
        var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();

        try
        {
            var body = args.Message.Body.ToString();
            var eventType = DetermineEventType(args.Message);

            if (eventType is null)
            {
                logger.LogWarning(
                    "Message has an unknown or missing EventType. Dead-lettering.");
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "UnknownEventType",
                    deadLetterErrorDescription: "Could not determine event type from message properties or body.",
                    cancellationToken: args.CancellationToken);
                return;
            }

            Guid eventId;
            try
            {
                eventId = ExtractEventId(body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Message body could not be deserialized. Dead-lettering.");
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: ex.Message,
                    cancellationToken: args.CancellationToken);
                return;
            }

            // Inbox check — idempotent processing.
            if (await inboxStore.HasBeenProcessedAsync(eventId, args.CancellationToken))
            {
                logger.LogInformation("Event {EventId} already processed (inbox). Completing message.", eventId);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            // Map integration event type to webhook domain event type.
            var webhookEventType = MapToWebhookEventType(eventType);
            if (webhookEventType is null)
            {
                logger.LogWarning(
                    "Message has event type '{EventType}' with no webhook mapping. Dead-lettering.",
                    messageId, eventType);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "UnknownEventType",
                    deadLetterErrorDescription: $"No webhook mapping for event type '{eventType}'.",
                    cancellationToken: args.CancellationToken);
                return;
            }

            // Find all active subscriptions that listen to this event type.
            var eventTypeName = webhookEventType.Value.ToString();
            var subscriptions = await db.Subscriptions
                .Where(s => s.IsActive && s.Events.Contains(eventTypeName))
                .ToListAsync(args.CancellationToken);

            // Filter precisely (Contains is a substring match; verify exact enum membership).
            var matchingSubscriptions = subscriptions
                .Where(s => s.ListensTo(webhookEventType.Value))
                .ToList();

            if (matchingSubscriptions.Count == 0)
            {
                logger.LogDebug("No active subscriptions for event type {EventType}.", eventTypeName);
                await inboxStore.MarkAsProcessedAsync(eventId, eventType, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            // Create a delivery record for each matching subscription.
            foreach (var sub in matchingSubscriptions)
            {
                var delivery = new WebhookDelivery(
                    sub.Id,
                    eventTypeName,
                    eventId,
                    body);

                db.Deliveries.Add(delivery);
            }

            await db.SaveChangesAsync(args.CancellationToken);
            await inboxStore.MarkAsProcessedAsync(eventId, eventType, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);

            logger.LogInformation(
                "Created {Count} webhook deliveries for event {EventId} ({EventType}).",
                matchingSubscriptions.Count, eventId, eventTypeName);
        }
        catch (Exception ex)
        {
            logger.MessageProcessingFailed(ex);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Service Bus processor error. Source: {Source}, Entity: {Entity}",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    private static string? DetermineEventType(ServiceBusReceivedMessage message)
    {
        if (message.ApplicationProperties.TryGetValue("EventType", out var et) && et is string eventType)
            return eventType;

        // Try to infer from body.
        try
        {
            using var doc = JsonDocument.Parse(message.Body.ToString());
            if (doc.RootElement.TryGetProperty("Type", out var typeProp))
                return typeProp.GetString();
        }
        catch { /* Not JSON or missing property — treated as unknown. */ }

        return null;
    }

    private static Guid ExtractEventId(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("Id", out var idProp))
            return idProp.GetGuid();

        return Guid.NewGuid();
    }

    private static WebhookEventType? MapToWebhookEventType(string eventType) => eventType switch
    {
        nameof(OrderPlacedIntegrationEvent) or "OrderPlaced" => WebhookEventType.OrderPlaced,
        nameof(OrderStatusChangedIntegrationEvent) or "OrderStatusChanged" => WebhookEventType.OrderStatusChanged,
        nameof(PaymentProcessedIntegrationEvent) or "PaymentCompleted" => WebhookEventType.PaymentCompleted,
        "PaymentFailed" => WebhookEventType.PaymentFailed,
        _ => null,
    };
}
