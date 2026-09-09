using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;

/// <summary>
/// Distributed-tracing helpers for the Service Bus event pipeline. Propagates the W3C trace
/// context across the asynchronous outbox → queue → consumer boundary so that a single trace
/// spans every hop (HTTP request → outbox → publish → consume → outbox → …).
///
/// Uses only the BCL <see cref="ActivitySource"/>/<see cref="Activity"/> API (no OpenTelemetry
/// package dependency). The source is registered in ServiceDefaults via
/// <c>tracing.AddSource(EventBusDiagnostics.SourceName)</c>.
/// </summary>
public static class EventBusDiagnostics
{
    /// <summary>ActivitySource name. Register this in the tracer provider to record the spans.</summary>
    public const string SourceName = "OrderSphere.EventBus";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>W3C trace-context header carried as a Service Bus application property.</summary>
    private const string TraceParentProperty = "traceparent";

    /// <summary>
    /// Log-correlation id carried alongside the trace context. Distinct from the trace id: it
    /// survives sampling and is the value operators quote from a client-facing error. Distinct
    /// also from <c>IntegrationEvent.CorrelationId</c>, which is a business idempotency key.
    /// </summary>
    private const string CorrelationIdProperty = "x-request-id";

    // The OutboxDispatcher publishes on a timer, long after the originating request/consume
    // completed, so the original context is no longer on Activity.Current. Both the trace context
    // and the log-correlation id are persisted on the outbox row and restored here — the trace as
    // an ambient parent for the publish span, the correlation id as an ambient scope.
    private static readonly AsyncLocal<ActivityContext?> AmbientPublishParent = new();

    /// <summary>
    /// Restores the originating trace context and log-correlation id (both captured when the
    /// outbox row was written) so the publish span and everything downstream rejoin the original
    /// trace <em>and</em> the original <c>correlation_id</c>. Dispose to clear both.
    /// </summary>
    /// <param name="correlationId">
    /// The persisted log-correlation id. Pass <see langword="null"/> only for rows written before
    /// the column existed; the trace id is then used, which is what those rows were correlated by.
    /// </param>
    public static IDisposable RestorePublishParent(string? traceParent, string? correlationId)
    {
        var previous = AmbientPublishParent.Value;
        var parsed = ActivityContext.TryParse(traceParent, null, isRemote: true, out var ctx);
        AmbientPublishParent.Value = parsed ? ctx : null;

        // Prefer the id that was actually ambient when the row was written. Deriving it from the
        // trace id is only correct while correlation_id == trace_id, which the gateway breaks by
        // honouring a client-supplied X-Request-Id and a consumer breaks by falling back to the
        // message id — so the derivation is now the legacy fallback, not the rule.
        //
        // This also opens a scope when there is no usable trace context at all, which the
        // previous version did not: without it Inject() below omitted x-request-id entirely, the
        // consumer fell through to the message id, and that value was then persisted on the next
        // outbox hop — permanently severing the chain.
        var resolved = correlationId is { Length: > 0 }
            ? correlationId
            : parsed ? ctx.TraceId.ToString() : null;

        var correlationScope = resolved is null
            ? null
            : AmbientCorrelationContext.BeginScope(resolved);

        return new ParentScope(previous, correlationScope);
    }

    /// <summary>Starts a producer span for a publish to <paramref name="destination"/>.</summary>
    public static Activity? StartPublish(string destination, string? messageId)
    {
        var activity = AmbientPublishParent.Value is { } parent
            ? Source.StartActivity($"{destination} publish", ActivityKind.Producer, parent)
            : Source.StartActivity($"{destination} publish", ActivityKind.Producer);

        SetMessagingTags(activity, destination, "publish", messageId);
        return activity;
    }

    /// <summary>Writes the current trace context onto the outgoing message.</summary>
    public static void Inject(ServiceBusMessage message)
    {
        // Prefer the active producer span; fall back to the restored ambient parent so the trace
        // id still propagates when the source is not sampled (StartPublish returned null).
        var traceParent = Activity.Current?.Id ?? FormatTraceParent(AmbientPublishParent.Value);
        if (traceParent is not null)
            message.ApplicationProperties[TraceParentProperty] = traceParent;

        if (AmbientCorrelationContext.Ambient is { Length: > 0 } correlationId)
            message.ApplicationProperties[CorrelationIdProperty] = correlationId;
    }

    /// <summary>
    /// Reads the log-correlation id carried on an inbound message.
    /// <para>
    /// The two fallbacks are for messages that predate the correlation property, not statements
    /// that the values are equivalent: the trace id correlates such a message to its originating
    /// operation, and the message id is a last resort that at least keeps the records of one
    /// message together. A message published by current code always carries the property, because
    /// every publishing path now has an ambient correlation id to inject.
    /// </para>
    /// </summary>
    public static string ReadCorrelationId(ServiceBusReceivedMessage message)
    {
        if (message.ApplicationProperties.TryGetValue(CorrelationIdProperty, out var raw)
            && raw is string correlationId
            && correlationId.Length > 0)
        {
            return correlationId;
        }

        if (message.ApplicationProperties.TryGetValue(TraceParentProperty, out var rawTrace)
            && rawTrace is string traceParent
            && ActivityContext.TryParse(traceParent, null, isRemote: true, out var ctx))
        {
            return ctx.TraceId.ToString();
        }

        return message.MessageId;
    }

    /// <summary>Starts a consumer span linked to the producer context carried by the message.</summary>
    public static Activity? StartProcess(ServiceBusReceivedMessage message, string queueName)
    {
        Activity? activity =
            message.ApplicationProperties.TryGetValue(TraceParentProperty, out var raw)
            && raw is string traceParent
            && ActivityContext.TryParse(traceParent, null, isRemote: true, out var ctx)
                ? Source.StartActivity($"{queueName} process", ActivityKind.Consumer, ctx)
                : Source.StartActivity($"{queueName} process", ActivityKind.Consumer);

        SetMessagingTags(activity, queueName, "process", message.MessageId);
        return activity;
    }

    private static void SetMessagingTags(Activity? activity, string destination, string operation, string? messageId)
    {
        if (activity is null)
            return;

        activity.SetTag("messaging.system", "servicebus");
        activity.SetTag("messaging.destination.name", destination);
        activity.SetTag("messaging.operation", operation);
        if (messageId is not null)
            activity.SetTag("messaging.message.id", messageId);
    }

    private static string? FormatTraceParent(ActivityContext? context)
    {
        if (context is not { } ctx)
            return null;

        var sampled = (ctx.TraceFlags & ActivityTraceFlags.Recorded) != 0 ? "01" : "00";
        return $"00-{ctx.TraceId}-{ctx.SpanId}-{sampled}";
    }

    private sealed class ParentScope(ActivityContext? previous, IDisposable? correlationScope) : IDisposable
    {
        public void Dispose()
        {
            correlationScope?.Dispose();
            AmbientPublishParent.Value = previous;
        }
    }
}
