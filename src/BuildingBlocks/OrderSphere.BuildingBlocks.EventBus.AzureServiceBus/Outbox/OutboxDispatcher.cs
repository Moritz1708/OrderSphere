using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Diagnostics;
using OrderSphere.BuildingBlocks.EventBus.Outbox;
using OrderSphere.BuildingBlocks.Locking;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Outbox;

public sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    IDistributedLock distributedLock,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingAsync(stoppingToken);
            await Task.Delay(PollingInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        var lockKey = $"outbox-dispatch:{typeof(TContext).Name}";
        await using var handle = await distributedLock.TryAcquireAsync(lockKey, LockTtl, ct);
        if (handle is null)
            return;

        // Wraps the whole batch, not just the dispatch of one row: the poison-count warning, the
        // per-message retry/permanent-failure records and the transient loop error below all sit
        // outside RestorePublishParent, and they are precisely the records wanted when a flow has
        // stalled. Per-row context nests inside this scope in DispatchAsync.
        using var operation = BackgroundOperationScope.Begin("outbox-dispatch");

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            var handlers = scope.ServiceProvider
                .GetRequiredService<IEnumerable<IOutboxEventHandler>>()
                .ToDictionary(h => h.EventType);

            var poisonCount = await context.Set<OutboxMessage>()
                .CountAsync(m => m.ProcessedAt == null && m.RetryCount >= OutboxMessage.MaxRetries, ct);

            if (poisonCount > 0)
                logger.LogWarning(
                    "{Count} outbox message(s) permanently failed (RetryCount >= {Max}). Inspect the Error column in outbox_messages.",
                    poisonCount, OutboxMessage.MaxRetries);

            var messages = await context.Set<OutboxMessage>()
                .Where(m => m.ProcessedAt == null && m.RetryCount < OutboxMessage.MaxRetries)
                .OrderBy(m => m.OccurredAt)
                .Take(20)
                .ToListAsync(ct);

            if (messages.Count == 0)
                return;

            foreach (var message in messages)
            {
                try
                {
                    await DispatchAsync(message, handlers, ct);
                    message.ProcessedAt = DateTime.UtcNow;
                    message.Error = null;
                    OutboxMetrics.Published.Add(1);
                }
                catch (Exception ex)
                {
                    message.RetryCount++;
                    message.Error = ex.Message;

                    if (message.RetryCount >= OutboxMessage.MaxRetries)
                    {
                        OutboxMetrics.Poison.Add(1);
                        logger.LogError(ex,
                            "Outbox message {Id} ({Type}) permanently failed after {MaxRetries} attempts.",
                            message.Id, message.Type, OutboxMessage.MaxRetries);
                    }
                    else
                        logger.LogWarning(ex,
                            "Outbox message {Id} ({Type}) failed on attempt {Attempt}/{Max}. Will retry.",
                            message.Id, message.Type, message.RetryCount, OutboxMessage.MaxRetries);
                }
            }

            await context.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OutboxDispatcher: transient error in ProcessPendingAsync. Loop continues.");
        }
    }

    private static async Task DispatchAsync(
        OutboxMessage message,
        Dictionary<string, IOutboxEventHandler> handlers,
        CancellationToken ct)
    {
        if (!handlers.TryGetValue(message.Type, out var handler))
            throw new InvalidOperationException(
                $"No handler registered for outbox event type '{message.Type}'. " +
                $"Registered types: {string.Join(", ", handlers.Keys)}");

        // Restore the originating trace context and correlation id so the publish (which happens
        // here, on the dispatcher's timer) rejoins both the trace and the correlation chain that
        // produced the outbox row.
        using (EventBusDiagnostics.RestorePublishParent(message.TraceParent, message.CorrelationId))
        {
            await handler.HandleAsync(message.Content, ct);
        }
    }
}
