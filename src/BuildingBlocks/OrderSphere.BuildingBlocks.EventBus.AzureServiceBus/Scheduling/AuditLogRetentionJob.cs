using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.Scheduling;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;

/// <summary>
/// Deletes <see cref="AuditLogEntry"/> rows older than the retention window
/// (<c>Retention:AuditLogDays</c>, default 180). The audit trail has no cleanup mechanism today —
/// every tracked change is written and never removed. Register via
/// <c>AddScheduledJob&lt;AuditLogRetentionJob&lt;TContext&gt;&gt;()</c>.
/// </summary>
public sealed class AuditLogRetentionJob<TContext>(
    TContext context,
    IConfiguration configuration,
    ILogger<AuditLogRetentionJob<TContext>> logger) : IScheduledJob
    where TContext : DbContext
{
    public static TimeSpan Interval => TimeSpan.FromDays(1);
    public static string LockResource => $"auditlog-retention:{typeof(TContext).Name}";

    public async Task RunAsync(CancellationToken ct)
    {
        var retentionDays = configuration.GetValue("Retention:AuditLogDays", 180);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        var deleted = await context.Set<AuditLogEntry>()
            .Where(e => e.OccurredAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation(
                "AuditLogRetention removed {Count} audit log entries older than {Days} days.",
                deleted, retentionDays);
    }
}
