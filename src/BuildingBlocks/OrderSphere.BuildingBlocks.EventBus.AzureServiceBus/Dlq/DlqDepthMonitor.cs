using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Diagnostics;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;

/// <summary>
/// Periodically polls the dead-letter depth of every owned queue into <see cref="DlqDepthCache"/>,
/// which backs the <c>ordersphere.dlq.depth</c> gauge. Polling failures are logged and swallowed so
/// a transient Service Bus error never stops the host.
/// </summary>
internal sealed class DlqDepthMonitor(
    IDlqAdmin admin,
    DlqDepthCache cache,
    DlqAdminOptions options,
    ILogger<DlqDepthMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.DepthPollInterval);

        do
        {
            using var operation = BackgroundOperationScope.Begin("dlq-depth-poll");

            try
            {
                var depths = await admin.GetDepthsAsync(stoppingToken);
                if (depths.IsSuccess)
                {
                    foreach (var depth in depths.Value)
                        cache.Set(depth.Queue, depth.Depth);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Dead-letter depth poll failed; gauge keeps the last value.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
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
