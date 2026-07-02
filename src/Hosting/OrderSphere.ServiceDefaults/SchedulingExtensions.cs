using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Diagnostics;
using OrderSphere.BuildingBlocks.Locking;
using OrderSphere.BuildingBlocks.Scheduling;

namespace Microsoft.Extensions.Hosting;

public static class SchedulingExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TJob"/> as a scoped service and hosts it behind
    /// <see cref="ScheduledJobRunner{TJob}"/>, which leases <see cref="IScheduledJob.LockResource"/>
    /// via <see cref="IDistributedLock"/> before each run so exactly one replica executes it per
    /// <see cref="IScheduledJob.Interval"/>. Falls back to <see cref="NullDistributedLock"/> if no
    /// distributed lock has been registered (single-instance dev/test).
    /// </summary>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services)
        where TJob : class, IScheduledJob
    {
        services.TryAddSingleton<IDistributedLock>(NullDistributedLock.Instance);
        services.AddScoped<TJob>();
        services.AddHostedService<ScheduledJobRunner<TJob>>();
        return services;
    }
}

/// <summary>
/// Generic host for an <see cref="IScheduledJob"/>: waits <see cref="IScheduledJob.Interval"/>,
/// leases the job's distributed lock, resolves the job in its own DI scope, runs it, and records
/// <c>ordersphere.job.*</c> metrics. A lease miss (another replica already holds it) is silently
/// skipped for that cycle.
/// </summary>
public sealed class ScheduledJobRunner<TJob>(
    IServiceScopeFactory scopeFactory,
    IDistributedLock distributedLock,
    ILogger<ScheduledJobRunner<TJob>> logger) : BackgroundService
    where TJob : class, IScheduledJob
{
    private static readonly string JobName = typeof(TJob).Name;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TJob.Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled job {Job} failed.", JobName);
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var handle = await distributedLock.TryAcquireAsync(TJob.LockResource, TJob.Interval, ct);
        if (handle is null)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();

        var stopwatch = Stopwatch.StartNew();
        var succeeded = false;
        try
        {
            await job.RunAsync(ct);
            succeeded = true;
        }
        finally
        {
            ApplicationDiagnostics.JobDuration.Record(
                stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("job", JobName));
            ApplicationDiagnostics.JobRuns.Add(1,
                new KeyValuePair<string, object?>("job", JobName),
                new KeyValuePair<string, object?>("outcome", succeeded ? "success" : "failure"));
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
