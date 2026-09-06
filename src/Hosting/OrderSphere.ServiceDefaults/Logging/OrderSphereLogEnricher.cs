using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.Enrichment;
using OrderSphere.BuildingBlocks.Security;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Attaches request-scoped identifiers to every log record produced by the process.
/// <para>
/// Tenant and correlation are read from <see cref="AsyncLocal{T}"/> slots rather than from
/// <c>HttpContext</c>, so the same enricher covers API requests and worker message loops.
/// That is the reason worker logs carry the same field set as API logs — the message loop
/// opens the ambient scopes (see <c>MessageProcessingScope</c>) and this enricher picks them up.
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
        if (AmbientTenantContext.Ambient is { } tenantId)
        {
            collector.Add("tenant_id", tenantId);
        }

        if (AmbientCorrelationContext.Ambient is { Length: > 0 } correlationId)
        {
            collector.Add("correlation_id", correlationId);
        }

        // Auth0 "sub" — an opaque pseudonymous identifier. Logged verbatim by design so that
        // operators can trace a single user's requests (documented in docs/operations.md);
        // the directly identifying attributes behind it live in UserProfile, not in logs.
        var user = httpContextAccessor.HttpContext?.User;
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
