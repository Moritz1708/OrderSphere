using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.Scheduling;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;

/// <summary>
/// Deletes processed <see cref="InboxMessage"/> rows older than the retention window
/// (<c>Retention:InboxDays</c>, default 30). Without this, the idempotency table grows
/// unbounded — <see cref="OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Inbox.EfInboxStore{TContext}"/>
/// writes one row per consumed event and never removes them.
/// Register via <c>AddScheduledJob&lt;InboxCleanupJob&lt;TContext&gt;&gt;()</c>.
/// </summary>
public sealed class InboxCleanupJob<TContext>(
    TContext context,
    IConfiguration configuration,
    ILogger<InboxCleanupJob<TContext>> logger) : IScheduledJob
    where TContext : DbContext
{
    public static TimeSpan Interval => TimeSpan.FromDays(1);
    public static string LockResource => $"inbox-cleanup:{typeof(TContext).Name}";

    public async Task RunAsync(CancellationToken ct)
    {
        var retentionDays = configuration.GetValue("Retention:InboxDays", 30);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var deleted = await context.Set<InboxMessage>()
            .Where(m => m.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation(
                "InboxCleanup removed {Count} processed inbox messages older than {Days} days.",
                deleted, retentionDays);
    }
}
