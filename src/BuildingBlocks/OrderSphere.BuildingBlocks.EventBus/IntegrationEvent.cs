using OrderSphere.BuildingBlocks.Security;

namespace OrderSphere.BuildingBlocks.EventBus;

public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Tenant the event originated from (ADR 0012). Defaults to whatever tenant is ambient at
    /// construction time (see <see cref="AmbientTenantContext"/>), so an event constructed inside
    /// a worker's <c>AmbientTenantContext.BeginScope(...)</c> — e.g. while reacting to an inbound
    /// event — automatically carries the tenant forward to any events it stages, with no per-call
    /// site wiring. This does <em>not</em> read the current HTTP request's claim: a command handler
    /// publishing the first event in a flow must open its own
    /// <c>AmbientTenantContext.BeginScope(tenantContext.TenantId)</c> (or set this property
    /// explicitly) before constructing the event, since no ambient scope is open by default in an
    /// API request.
    /// </summary>
    public Guid TenantId { get; init; } = AmbientTenantContext.Ambient ?? StronglyTypedIds.TenantId.Default;
}
