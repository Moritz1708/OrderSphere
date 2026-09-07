namespace OrderSphere.BuildingBlocks.Security;

/// <summary>
/// Ambient tenant slot for the whole system (ADR 0012). Two callers open it, and between them
/// they cover every path on which tenant-scoped data is read or written:
/// <list type="bullet">
/// <item><c>RequestContextEnrichmentMiddleware</c> (ServiceDefaults) opens it per HTTP request
/// from the Auth0 <c>org_id</c> claim, after authentication and for the whole of endpoint
/// execution.</item>
/// <item><c>MessageProcessingScope.SetTenant</c> opens it per Service Bus message from the
/// <c>TenantId</c> carried on the integration event, before any persistence code runs.</item>
/// </list>
/// Disposing a scope restores the previous value, so it never leaks onto an unrelated request or
/// message on the same thread/continuation.
/// <para>
/// Because the slot is <see cref="AsyncLocal{T}"/>, work that severs the execution context —
/// <c>Task.Run</c>, <c>Task.Factory.StartNew</c>, <c>ExecutionContext.SuppressFlow</c> — silently
/// drops the tenant back to <c>TenantId.Default</c>. Do not start detached work between an
/// endpoint and the code that persists or publishes.
/// </para>
/// <see cref="Ambient"/> is read by the shared <c>ITenantContext</c> implementation (see
/// <c>HttpContextTenantContext</c> in ServiceDefaults) in preference to any HTTP claim, and by
/// <c>IntegrationEvent.TenantId</c>, so one scope covers logging, EF stamping, the tenant query
/// filter and event construction alike.
/// </summary>
public static class AmbientTenantContext
{
    private static readonly AsyncLocal<Guid?> Current = new();

    /// <summary>The ambient tenant set by the innermost open <see cref="BeginScope"/>, if any.</summary>
    public static Guid? Ambient => Current.Value;

    public static IDisposable BeginScope(Guid tenantId)
    {
        var previous = Current.Value;
        Current.Value = tenantId;
        return new Scope(previous);
    }

    private sealed class Scope(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
