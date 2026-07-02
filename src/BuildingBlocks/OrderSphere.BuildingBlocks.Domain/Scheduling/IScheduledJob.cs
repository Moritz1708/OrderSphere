namespace OrderSphere.BuildingBlocks.Scheduling;

/// <summary>
/// A unit of work run periodically by <c>ScheduledJobRunner&lt;TJob&gt;</c> (ServiceDefaults), which
/// leases <see cref="LockResource"/> via <c>IDistributedLock</c> before each run so exactly one
/// replica executes the job per interval.
/// </summary>
public interface IScheduledJob
{
    /// <summary>How often the job runs, and the lease TTL requested for that run.</summary>
    static abstract TimeSpan Interval { get; }

    /// <summary>Distributed lock resource name. Must be unique per job across the process.</summary>
    static abstract string LockResource { get; }

    Task RunAsync(CancellationToken ct);
}
