using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Diagnostics;
using OrderSphere.BuildingBlocks.Locking;
using OrderSphere.Webhooks.Domain.Enums;
using OrderSphere.Webhooks.Infrastructure.Persistence;

namespace OrderSphere.Webhooks.Worker.Workers;

/// <summary>
/// Periodically polls for pending or retryable webhook deliveries and sends them
/// to the target URL with an HMAC-SHA256 signature header.
///
/// Delivery contract:
///   POST {subscription.Url}
///   Content-Type: application/json
///   X-Webhook-Signature: sha256={hex}
///   X-Webhook-Event: {eventType}
///   X-Webhook-Delivery: {deliveryId}
///
/// The signature is HMAC-SHA256(secret, rawPayloadBytes) encoded as lowercase hex.
/// </summary>
public sealed class WebhookDeliveryProcessor(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IDistributedLock distributedLock,
    ILogger<WebhookDeliveryProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the API time to create the database/schema before polling.
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Covers the whole batch, including the outbound HTTP calls. Beyond grouping this
            // iteration's records, it gives CorrelationPropagationHandler an ambient id to put on
            // the "WebhookDelivery" client — the handler was already registered but never fired
            // here, because nothing had opened a correlation scope, so a partner's failed
            // delivery carried no id linking it back to the event that produced it.
            using (BackgroundOperationScope.Begin("webhook-delivery"))
            {
                try
                {
                    var delivered = await ProcessPendingDeliveriesAsync(stoppingToken);
                    if (delivered == 0)
                        await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Unhandled error in webhook delivery loop.");
                    await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
        }
    }

    private async Task<int> ProcessPendingDeliveriesAsync(CancellationToken ct)
    {
        await using var handle = await distributedLock.TryAcquireAsync(
            "webhooks:delivery", PollInterval * 10, ct);
        if (handle is null)
            return 0;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();

        var now = DateTime.UtcNow;

        var deliveries = await db.Deliveries
            .Where(d => d.Status == DeliveryStatus.Pending
                || (d.Status != DeliveryStatus.Succeeded
                    && d.Status != DeliveryStatus.Failed
                    && d.NextRetryAt != null
                    && d.NextRetryAt <= now))
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (deliveries.Count == 0) return 0;

        // Load the subscription secrets for all deliveries in one query.
        var subscriptionIds = deliveries.Select(d => d.SubscriptionId).Distinct().ToList();
        var subscriptions = await db.Subscriptions
            .Where(s => subscriptionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var httpClient = httpClientFactory.CreateClient("WebhookDelivery");

        foreach (var delivery in deliveries)
        {
            if (!subscriptions.TryGetValue(delivery.SubscriptionId, out var sub))
            {
                delivery.RecordFailure(null, "Subscription not found.");
                continue;
            }

            if (!sub.IsActive || sub.IsDeleted)
            {
                delivery.RecordFailure(null, "Subscription deactivated or deleted.");
                continue;
            }

            await DeliverAsync(httpClient, delivery, sub, ct);
        }

        await db.SaveChangesAsync(ct);
        return deliveries.Count;
    }

    private async Task DeliverAsync(
        HttpClient httpClient,
        Domain.Entities.WebhookDelivery delivery,
        Domain.Entities.WebhookSubscription subscription,
        CancellationToken ct)
    {
        try
        {
            var payloadBytes = Encoding.UTF8.GetBytes(delivery.Payload);
            var signature = ComputeSignature(subscription.Secret, payloadBytes);

            using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
            {
                Content = new ByteArrayContent(payloadBytes)
                {
                    Headers = { { "Content-Type", "application/json" } }
                }
            };

            request.Headers.TryAddWithoutValidation("X-Webhook-Signature", $"sha256={signature}");
            request.Headers.TryAddWithoutValidation("X-Webhook-Event", delivery.EventType);
            request.Headers.TryAddWithoutValidation("X-Webhook-Delivery", delivery.Id.ToString());

            using var response = await httpClient.SendAsync(request, ct);

            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                delivery.RecordSuccess(statusCode);
                WebhookMetrics.Dispatched.Add(1, new KeyValuePair<string, object?>("status", statusCode));
                logger.LogInformation(
                    "Webhook delivery {DeliveryId} to {Url} succeeded with {StatusCode}.",
                    delivery.Id, subscription.Url, statusCode);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var error = $"HTTP {statusCode}: {body}";
                delivery.RecordFailure(statusCode, error);
                WebhookMetrics.Failed.Add(1, new KeyValuePair<string, object?>("status", statusCode));
                logger.LogWarning(
                    "Webhook delivery {DeliveryId} to {Url} failed with {StatusCode}. Attempt {Attempt}/{Max}.",
                    delivery.Id, subscription.Url, statusCode,
                    delivery.AttemptCount, Domain.Entities.WebhookDelivery.MaxAttempts);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.RecordFailure(null, ex.Message);
            WebhookMetrics.Failed.Add(1, new KeyValuePair<string, object?>("status", "exception"));
            logger.LogWarning(ex,
                "Webhook delivery {DeliveryId} to {Url} threw exception. Attempt {Attempt}/{Max}.",
                delivery.Id, subscription.Url,
                delivery.AttemptCount, Domain.Entities.WebhookDelivery.MaxAttempts);
        }
    }

    private static string ComputeSignature(string secret, byte[] payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(keyBytes, payload);
        return Convert.ToHexStringLower(hash);
    }
}
