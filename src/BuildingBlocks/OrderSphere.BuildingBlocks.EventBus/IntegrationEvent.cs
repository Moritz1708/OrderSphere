using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.BuildingBlocks.EventBus;

public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Tenant the event originated from (ADR 0012). Defaults to whatever tenant is ambient at
    /// construction time (see <see cref="AmbientTenantContext"/>).
    /// <para>
    /// Both paths that construct events open that scope for you, so this default is correct
    /// without per-call-site wiring: <c>RequestContextEnrichmentMiddleware</c> opens it from the
    /// <c>org_id</c> claim for the duration of an HTTP request — which spans the command handler
    /// that stages the event — and <c>MessageProcessingScope.SetTenant</c> opens it from the
    /// inbound event for the duration of a message loop, carrying the tenant forward into any
    /// event that loop stages in turn.
    /// </para>
    /// <para>
    /// Assigning this property explicitly is therefore a smell: it means either the ambient scope
    /// is missing where it should not be, or the caller is overriding the originating tenant.
    /// Fix the scope instead. The value falls back to <c>TenantId.Default</c> for genuinely
    /// tenant-less flows (anonymous requests, background jobs), which is what that value means.
    /// </para>
    /// </summary>
    public Guid TenantId { get; init; } = AmbientTenantContext.Ambient ?? StronglyTypedIds.TenantId.Default;
}
