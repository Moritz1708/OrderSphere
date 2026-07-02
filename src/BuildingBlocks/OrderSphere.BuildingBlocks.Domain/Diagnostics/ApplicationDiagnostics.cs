using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OrderSphere.BuildingBlocks.Diagnostics;

/// <summary>
/// Shared diagnostics primitives. The meter name <see cref="MeterName"/> ("OrderSphere") is the
/// single registration handle for every custom OrderSphere metric — registered once in
/// ServiceDefaults via <c>metrics.AddMeter("OrderSphere")</c>. The activity source surfaces a
/// span per MediatR request (CQRS handler) and is registered via
/// <c>tracing.AddSource("OrderSphere.Application")</c>.
/// </summary>
public static class ApplicationDiagnostics
{
    public const string MeterName = "OrderSphere";
    public const string ActivitySourceName = "OrderSphere.Application";

    public static readonly Meter Meter = new(MeterName);
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>MediatR request handler duration in milliseconds, tagged by request and outcome.</summary>
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "ordersphere.mediatr.request.duration",
        unit: "ms",
        description: "MediatR request handler duration.");

    /// <summary>Advisory agent semantic cache hits.</summary>
    public static readonly Counter<long> AdvisorCacheHits = Meter.CreateCounter<long>(
        "ordersphere.advisor.cache.hits",
        description: "Number of advisory turns served from the semantic cache.");

    /// <summary>Advisory agent semantic cache misses (query forwarded to Foundry).</summary>
    public static readonly Counter<long> AdvisorCacheMisses = Meter.CreateCounter<long>(
        "ordersphere.advisor.cache.misses",
        description: "Number of advisory turns that bypassed the semantic cache.");

    /// <summary>Wall-clock duration of a model-backed advisory turn, tagged by outcome (completed|canceled|failed).</summary>
    public static readonly Histogram<double> AdvisorTurnDuration = Meter.CreateHistogram<double>(
        "ordersphere.advisor.turn.duration",
        unit: "ms",
        description: "Wall-clock duration of a model-backed advisory turn.");

    /// <summary>Model tokens consumed by the advisory agent, tagged by direction (input|output).</summary>
    public static readonly Counter<long> AdvisorTokens = Meter.CreateCounter<long>(
        "ordersphere.advisor.tokens",
        unit: "{token}",
        description: "Model tokens consumed by the advisory agent, tagged by direction.");

    /// <summary>Advisory agent tool invocations, tagged by tool and outcome (success|error).</summary>
    public static readonly Counter<long> AdvisorToolInvocations = Meter.CreateCounter<long>(
        "ordersphere.advisor.tool.invocations",
        description: "Advisory agent tool invocations, tagged by tool and outcome.");

    /// <summary>Scheduled job runs (ScheduledJobRunner), tagged by job and outcome (success|failure).</summary>
    public static readonly Counter<long> JobRuns = Meter.CreateCounter<long>(
        "ordersphere.job.runs",
        description: "Scheduled job runs, tagged by job and outcome.");

    /// <summary>Scheduled job execution duration, tagged by job.</summary>
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "ordersphere.job.duration",
        unit: "ms",
        description: "Scheduled job execution duration.");
}
