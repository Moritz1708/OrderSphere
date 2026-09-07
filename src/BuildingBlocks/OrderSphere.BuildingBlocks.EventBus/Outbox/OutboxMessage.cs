using System.Diagnostics;
using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.BuildingBlocks.EventBus.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Type { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }

    /// <summary>
    /// W3C traceparent captured when the row was written, so the asynchronously dispatched
    /// publish joins the originating trace. Null when no trace context was active.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>
    /// Log-correlation id ambient when the row was written (see
    /// <see cref="AmbientCorrelationContext"/>), so the asynchronously dispatched publish rejoins
    /// the originating <c>correlation_id</c> as well as the trace.
    /// <para>
    /// Persisted separately from <see cref="TraceParent"/> because the two are <em>not</em> the
    /// same value. The dispatcher used to derive correlation from the trace id, which held only
    /// as long as nothing chose a different correlation id — but the API Gateway honours a
    /// client-supplied <c>X-Request-Id</c>, and a consumer falls back to the Service Bus message
    /// id when a message carries no correlation property. Either one silently changed the
    /// correlation id at the outbox boundary and broke the chain a single Seq query is supposed
    /// to return.
    /// </para>
    /// <para>
    /// Null on rows written before this column existed; the dispatcher then falls back to the
    /// old trace-id derivation, which is correct for exactly those rows.
    /// </para>
    /// <para>
    /// Distinct from <c>IntegrationEvent.CorrelationId</c>, which is a business idempotency key.
    /// </para>
    /// </summary>
    public string? CorrelationId { get; init; }

    public const int MaxRetries = 10;

    /// <summary>
    /// Upper bound for <see cref="CorrelationId"/>. System-minted ids are 32-char hex (a trace id
    /// or a <c>Guid("N")</c>), but the value can originate from a client-supplied
    /// <c>X-Request-Id</c> header, so it is capped rather than trusted.
    /// </summary>
    public const int MaxCorrelationIdLength = 128;

    /// <summary>
    /// Creates a row capturing the caller's ambient trace and log-correlation context, so the
    /// asynchronously dispatched publish rejoins both.
    /// <para>
    /// Prefer this over an object initializer: capturing that context is the entire reason the
    /// three services' outbox rows correlate identically, and having one factory keeps the
    /// truncation rule and the captured field set from drifting between them.
    /// </para>
    /// </summary>
    public static OutboxMessage Create(string type, string content) => new()
    {
        Type = type,
        Content = content,
        TraceParent = Activity.Current?.Id,
        // Truncate rather than reject: this row is written inside the business transaction, so
        // an over-long header must not be able to fail an order over a diagnostics field.
        CorrelationId = AmbientCorrelationContext.Ambient is { Length: > 0 } id
            ? id[..Math.Min(id.Length, MaxCorrelationIdLength)]
            : null,
    };
}
