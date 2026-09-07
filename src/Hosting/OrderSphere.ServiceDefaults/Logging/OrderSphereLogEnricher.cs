using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.Enrichment;
using OrderSphere.BuildingBlocks.Security;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Attaches request-scoped identifiers to every log record produced by the process.
/// <para>
/// Tenant and correlation are read primarily from <see cref="AsyncLocal{T}"/> slots rather than
/// from <c>HttpContext</c>, so the same enricher covers API requests and worker message loops.
/// That is the reason worker logs carry the same field set as API logs — the message loop
/// opens the ambient scopes (see <c>MessageProcessingScope</c>, <c>BackgroundOperationScope</c>)
/// and this enricher picks them up. <c>HttpContext.Items</c> is consulted only as a fallback for
/// records written after those scopes have unwound; it is never populated off the HTTP path.
/// </para>
/// <para>
/// Registered as a singleton and invoked once per log record, so it must not depend on scoped
/// services such as <c>ICurrentUser</c>.
/// </para>
/// <para>
/// <c>trace_id</c> and <c>span_id</c> are emitted by the OpenTelemetry logger provider itself
/// and are deliberately not duplicated here.
/// </para>
/// </summary>
internal sealed class OrderSphereLogEnricher(IHttpContextAccessor httpContextAccessor) : ILogEnricher
{
    public void Enrich(IEnrichmentTagCollector collector)
    {
        // The ambient slots are authoritative and are the only source workers have. On the HTTP
        // path they can already be gone while the request is still being handled — an unhandled
        // exception unwinds past RequestContextEnrichmentMiddleware before the outer
        // ExceptionHandlerMiddleware logs it — so fall back to the values that middleware stashed
        // on the HttpContext, which outlives them. See RequestContextEnrichmentMiddleware.CorrelationItemKey.
        var httpContext = httpContextAccessor.HttpContext;

        if (AmbientTenantContext.Ambient is { } tenantId)
        {
            collector.Add("tenant_id", tenantId);
        }
        else if (httpContext?.Items.TryGetValue(
                     RequestContextEnrichmentMiddleware.TenantItemKey, out var stashedTenant) is true
                 && stashedTenant is Guid stashedTenantId)
        {
            collector.Add("tenant_id", stashedTenantId);
        }

        if (AmbientCorrelationContext.Ambient is { Length: > 0 } correlationId)
        {
            collector.Add("correlation_id", correlationId);
        }
        else if (httpContext?.Items.TryGetValue(
                     RequestContextEnrichmentMiddleware.CorrelationItemKey, out var stashed) is true
                 && stashed is string { Length: > 0 } stashedCorrelationId)
        {
            collector.Add("correlation_id", stashedCorrelationId);
        }

        // Auth0 "sub" — an opaque pseudonymous identifier. Logged verbatim by design so that
        // operators can trace a single user's requests (documented in docs/operations.md);
        // the directly identifying attributes behind it live in UserProfile, not in logs.
        var user = httpContext?.User;
        if (user is not null)
        {
            var userId = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId))
            {
                collector.Add("user_id", userId);
            }
        }
    }
}
