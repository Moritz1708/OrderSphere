using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Diagnostics;
using OrderSphere.BuildingBlocks.EventBus.Outbox;
using OrderSphere.BuildingBlocks.Locking;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Outbox;

public sealed class OutboxCleanupService<TContext>(
    IServiceScopeFactory scopeFactory,
    IDistributedLock distributedLock,
    ILogger<OutboxCleanupService<TContext>> logger,
    IConfiguration configuration) : BackgroundService
    where TContext : DbContext
{
    private static readonly TimeSpan _cleanupInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_cleanupInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await CleanupAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        var lockKey = $"outbox-cleanup:{typeof(TContext).Name}";
        await using var handle = await distributedLock.TryAcquireAsync(lockKey, TimeSpan.FromMinutes(5), ct);
        if (handle is null)
            return;

        using var operation = BackgroundOperationScope.Begin("outbox-cleanup");

        try
        {
            var retentionDays = configuration.GetValue("Outbox:RetentionDays", 7);
            var retention = TimeSpan.FromDays(retentionDays);

            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();

            var cutoff = DateTime.UtcNow - retention;
            var deleted = await context.Set<OutboxMessage>()
                .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                logger.LogInformation(
                    "OutboxCleanup removed {Count} processed outbox messages older than {Days} days.",
                    deleted, retentionDays);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OutboxCleanup failed.");
        }
    }
}
