using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSphere.BuildingBlocks.Contracts.Events;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Primitives;
using OrderSphere.BuildingBlocks.StronglyTypedIds;
using OrderSphere.Payment.Domain.Entities;
using OrderSphere.Payment.Domain.Enums;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Infrastructure.Providers;

namespace OrderSphere.Payment.Worker.Workers;

public sealed class PaymentProcessor(
    ServiceBusClient serviceBusClient,
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentOptions> options,
    ILogger<PaymentProcessor> logger) : BackgroundService
{
    private const string QueueName = "payment-requests";
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
        logger.ProcessorStarted(nameof(PaymentProcessor), QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None);
            logger.ProcessorStopped(nameof(PaymentProcessor));
        }
    }

    private async Task OnMessageReceived(ProcessMessageEventArgs args)
    {
        using var messageScope = MessageProcessingScope.Begin(logger, args.Message, QueueName);
        logger.MessageReceived();

        try
        {
            var evt = args.Message.Body.ToObjectFromJson<PaymentRequestedIntegrationEvent>();
            if (evt is null)
            {
                logger.MessageUndeserializable();
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "DeserializationFailed",
                    deadLetterErrorDescription: "Body was not a valid PaymentRequestedIntegrationEvent.");
                return;
            }

            messageScope.SetTenant(evt.TenantId);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var inboxStore = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            var providerFactory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

            if (await inboxStore.HasBeenProcessedAsync(evt.Id))
            {
                logger.DuplicateMessageIgnored();
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var sw = Stopwatch.StartNew();
            var record = await ProcessPaymentAsync(evt, context, providerFactory, args.CancellationToken);
            var succeeded = record.Status == PaymentStatus.Captured;
            sw.Stop();

            PaymentMetrics.Duration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("provider", evt.PaymentMethod));
            PaymentMetrics.Processed.Add(1,
                new KeyValuePair<string, object?>("provider", evt.PaymentMethod),
                new KeyValuePair<string, object?>("succeeded", succeeded));

            // Payment record, outbox message, and inbox entry are all written in one
            // SaveChangesAsync below — a single PostgreSQL transaction guarantees atomicity.
            // The OutboxDispatcher publishes to Service Bus asynchronously, so a crash
            // between Save and publish does not lose the event.
            EnqueuePaymentProcessedOutboxMessage(context, evt, record);
            await inboxStore.MarkAsProcessedAsync(evt.Id, nameof(PaymentRequestedIntegrationEvent));
            await context.SaveChangesAsync(args.CancellationToken);

            await args.CompleteMessageAsync(args.Message);
            logger.LogInformation("Payment message processed. OrderId: {OrderId}, Succeeded: {Succeeded}",
                evt.OrderId, succeeded);
        }
        catch (Exception ex)
        {
            logger.MessageProcessingFailed(ex);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    /// <summary>
    /// Runs authorize → capture for the order and returns the resulting record, staged but not
    /// saved. Provider exceptions (transient faults) propagate: nothing is persisted, the message
    /// is abandoned, and the redelivery replays the provider calls under the same idempotency keys.
    /// </summary>
    internal async Task<PaymentRecord> ProcessPaymentAsync(
        PaymentRequestedIntegrationEvent evt,
        PaymentDbContext context,
        IPaymentProviderFactory providerFactory,
        CancellationToken ct)
    {
        var existing = await context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == OrderId.From(evt.OrderId), ct);

        if (existing is not null)
        {
            logger.LogInformation("Payment for order {OrderId} already exists with status {Status}.",
                evt.OrderId, existing.Status);
            return existing;
        }

        var record = new PaymentRecord(
            OrderId.From(evt.OrderId),
            evt.Amount,
            evt.Currency,
            evt.PaymentMethod,
            evt.CustomerEmail,
            evt.CorrelationId);
        await context.Payments.AddAsync(record, ct);

        if (options.Value.BypassProviders)
        {
            var devTransactionId = $"DEV-{Guid.CreateVersion7():N}";
            Transition(record.MarkCaptured(devTransactionId));
            logger.LogInformation(
                "Provider bypass active — marking order {OrderId} as captured without contacting a provider. TransactionId: {TransactionId}",
                evt.OrderId, devTransactionId);
            return record;
        }

        var provider = providerFactory.GetProvider(evt.PaymentMethod);
        if (provider is null)
        {
            Transition(record.MarkFailed($"Unsupported payment method: {evt.PaymentMethod}"));
            return record;
        }

        var request = new PaymentRequest(
            evt.OrderId, evt.Amount, evt.Currency, evt.CustomerEmail, evt.TenantId, evt.CorrelationId);
        var authResult = await provider.AuthorizeAsync(request, ct);

        if (authResult.IsFailure)
        {
            Transition(record.MarkFailed(authResult.Error.Description ?? "Authorization failed."));
            return record;
        }

        // Stored before capture so a failed capture still leaves the provider reference on the
        // record — the Stripe webhook reconciles by it.
        var authorizationId = authResult.Value.TransactionId;
        Transition(record.MarkAuthorized(authorizationId));

        var captureResult = await provider.CaptureAsync(authorizationId, evt.Amount, ct);

        if (captureResult.IsFailure)
        {
            // Release the hold so the customer's funds are not blocked until the authorization lapses.
            var voided = await provider.VoidAsync(authorizationId, ct);
            if (voided.IsFailure)
                logger.LogWarning(
                    "Releasing authorization {TransactionId} for order {OrderId} failed; it lapses at the provider.",
                    authorizationId, evt.OrderId);

            Transition(record.MarkFailed(captureResult.Error.Description ?? "Capture failed."));
            return record;
        }

        Transition(record.MarkCaptured(captureResult.Value.TransactionId));

        logger.LogInformation("Payment captured for order {OrderId}. TransactionId: {TransactionId}",
            evt.OrderId, captureResult.Value.TransactionId);

        return record;
    }

    // Every transition above starts from a record this method just created, so a rejected
    // transition is a programming error, not a business outcome.
    private static void Transition(Result result)
    {
        if (result.IsFailure)
            throw new InvalidOperationException($"Invalid payment status transition: {result.Error.Code}");
    }

    internal static void EnqueuePaymentProcessedOutboxMessage(
        PaymentDbContext context,
        PaymentRequestedIntegrationEvent source,
        PaymentRecord record)
    {
        var succeeded = record.Status == PaymentStatus.Captured;
        var processed = new PaymentProcessedIntegrationEvent
        {
            CorrelationId = source.CorrelationId,
            OrderId = source.OrderId,
            Succeeded = succeeded,
            FailureReason = succeeded ? null : "Payment processing failed.",
            TransactionId = record.TransactionId,
            CustomerEmail = source.CustomerEmail,
            PaymentMethod = source.PaymentMethod
        };

        context.AddOutboxMessage(
            nameof(PaymentProcessedIntegrationEvent),
            JsonSerializer.Serialize(processed));
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
