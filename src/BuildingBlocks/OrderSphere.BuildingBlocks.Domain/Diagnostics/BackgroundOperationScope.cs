using System.Diagnostics;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.BuildingBlocks.Diagnostics;

/// <summary>
/// Gives one iteration of a timer-driven loop its own trace and log-correlation id.
/// <para>
/// Timer-driven work has no inbound request or message to inherit context from, so without this
/// its records carry no <c>trace_id</c>, no <c>correlation_id</c> and no way to tell one
/// iteration from the next. That is the gap this closes: the outbox dispatcher's own failure
/// records, the webhook delivery loop, the scheduled jobs and the DLQ monitor are exactly the
/// records an operator reaches for when a flow has stalled, and they were the ones a
/// <c>correlation_id</c> query could not return.
/// </para>
/// <para>
/// The correlation id is the new trace's id, matching how the API Gateway seeds
/// <c>X-Request-Id</c>, so background records read the same way as request records. This starts a
/// fresh root trace on purpose — an iteration is its own unit of work, not a continuation of
/// whatever produced the rows it happens to pick up. Per-item context (an outbox row's originating
/// trace, a message's tenant) is restored inside the iteration and nests under this scope.
/// </para>
/// </summary>
public static class BackgroundOperationScope
{
    /// <summary>
    /// ActivitySource name. Registered in ServiceDefaults via <c>tracing.AddSource(...)</c>;
    /// without that registration <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
    /// returns null and only the correlation id survives.
    /// </summary>
    public const string SourceName = "OrderSphere.Background";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// Opens the scope for one iteration. Dispose at the end of the iteration so the next one
    /// gets a fresh id rather than inheriting this one.
    /// </summary>
    /// <param name="operationName">
    /// Span name, and the unit of work being started — e.g. <c>"outbox-dispatch"</c>. Use a
    /// constant: it becomes a span name and must not carry per-iteration values.
    /// </param>
    public static IDisposable Begin(string operationName)
    {
        var activity = Source.StartActivity(operationName, ActivityKind.Internal);

        // Fall back to a fresh id when nothing is listening (source not registered, or sampled
        // out) so records still group per iteration even with no trace to hang them on.
        var correlationId = activity?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        return new Scope(activity, AmbientCorrelationContext.BeginScope(correlationId));
    }

    private sealed class Scope(Activity? activity, IDisposable correlationScope) : IDisposable
    {
        public void Dispose()
        {
            correlationScope.Dispose();
            activity?.Dispose();
        }
    }
}
