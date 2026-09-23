using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
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
        logger.ProcessorStarted(nameof(WebhookEventProcessor), QueueName);

        // Keep the service alive until shutdown is requested.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await _processor.StopProcessingAsync();
        logger.ProcessorStopped(nameof(WebhookEventProcessor));
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
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
                logger.MessageUndeserializable(ex);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: ex.Message,
                    cancellationToken: args.CancellationToken);
                return;
            }

            // Must be set before the Subscriptions query below: the tenant query filter reads
            // ITenantContext at query-execution time, so resolving the DbContext earlier is fine,
            // but executing a query before this line would scope it to the wrong tenant.
            messageScope.SetTenant(ExtractTenantId(body));

            // Inbox check — idempotent processing.
            if (await inboxStore.HasBeenProcessedAsync(eventId, args.CancellationToken))
            {
                logger.DuplicateMessageIgnored();
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            // Map integration event type to webhook domain event type.
            var webhookEventType = MapToWebhookEventType(eventType);
            if (webhookEventType is null)
            {
                logger.LogWarning(
                    "Message has event type '{EventType}' with no webhook mapping. Dead-lettering.",
                    eventType);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "UnknownEventType",
                    deadLetterErrorDescription: $"No webhook mapping for event type '{eventType}'.",
                    cancellationToken: args.CancellationToken);
                return;
            }

            // "No subscriber" is a normal outcome, not a special case: it takes the same path and
            // produces the same Information record with Count = 0. Previously it returned early
            // with only a Debug line, which made a completed message indistinguishable from a
            // processor that never ran — see docs/logging.md, one Information record per message.
            var created = await StageDeliveriesAsync(
                db, webhookEventType.Value, eventId, body, args.CancellationToken);

            await db.SaveChangesAsync(args.CancellationToken);
            await inboxStore.MarkAsProcessedAsync(eventId, eventType, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);

            logger.LogInformation(
                "Created {Count} webhook deliveries for event {EventId} ({EventType}).",
                created, eventId, webhookEventType.Value);
        }
        catch (Exception ex)
        {
            logger.MessageProcessingFailed(ex);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    /// <summary>
    /// Stages one delivery per active subscription that listens to the event type and belongs to
    /// the customer the event is about. Subscriptions are owned by a customer and the payload is
    /// that customer's data, so an event without a customer id (published before the property
    /// existed) is delivered to nobody rather than to every subscriber.
    /// </summary>
    internal static async Task<int> StageDeliveriesAsync(
        WebhooksDbContext db,
        WebhookEventType webhookEventType,
        Guid eventId,
        string body,
        CancellationToken ct)
    {
        if (ExtractCustomerId(body) is not { } customerId)
            return 0;

        var owner = CustomerId.From(customerId);
        var eventTypeName = webhookEventType.ToString();
        var subscriptions = await db.Subscriptions
            .Where(s => s.IsActive && s.CustomerId == owner && s.Events.Contains(eventTypeName))
            .ToListAsync(ct);

        // Contains is a substring match; verify exact enum membership.
        var matching = subscriptions.Where(s => s.ListensTo(webhookEventType)).ToList();
        foreach (var sub in matching)
            db.Deliveries.Add(new WebhookDelivery(sub.Id, eventTypeName, eventId, body));

        return matching.Count;
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.ProcessorError(args.Exception, args.EntityPath, args.ErrorSource.ToString());

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

    /// <summary>
    /// Reads the tenant off the raw event body. This processor never deserializes a typed event
    /// (it dispatches on the event type string), but every <c>IntegrationEvent</c> serializes
    /// <c>TenantId</c> from the base record, so the value is present on the wire.
    /// <para>
    /// Falls back to <c>TenantId.Default</c> rather than throwing: messages published before the
    /// tenant was propagated carry no usable value, and dead-lettering those would turn a
    /// diagnostics improvement into dropped webhook deliveries.
    /// </para>
    /// </summary>
    private static Guid ExtractTenantId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("TenantId", out var prop)
                && prop.TryGetGuid(out var tenantId))
            {
                return tenantId;
            }
        }
        catch { /* Body already validated as JSON by ExtractEventId; be defensive anyway. */ }

        return TenantId.Default;
    }

    /// <summary>Reads the optional <c>CustomerId</c> the publishing service puts on the event.</summary>
    private static Guid? ExtractCustomerId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("CustomerId", out var prop)
                && prop.ValueKind == JsonValueKind.String
                && prop.TryGetGuid(out var customerId)
                && customerId != Guid.Empty)
            {
                return customerId;
            }
        }
        catch { /* Body already validated as JSON by ExtractEventId; be defensive anyway. */ }

        return null;
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
