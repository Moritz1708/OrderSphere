using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Security;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Establishes the ambient correlation id and tenant for an HTTP request, and pushes the
/// request-local fields that are not process-wide into the log scope.
/// <para>
/// The correlation id is taken from the <c>X-Request-Id</c> header the API Gateway sets
/// (<c>ApiGateway/Program.cs</c>), so a value chosen at the edge survives every downstream hop.
/// Services reached directly fall back to the current trace id, and only mint a new id when
/// there is neither.
/// </para>
/// <para>
/// The tenant scope is this middleware's second job and is what makes ADR 0012 hold on the
/// request path. It is deliberately not only a logging concern: <c>ITenantContext</c>,
/// EF audit stamping, the tenant query filter and <c>IntegrationEvent.TenantId</c> all read the
/// same <see cref="AmbientTenantContext"/> slot, so opening it here is what carries the tenant
/// into events staged by a command handler. It must therefore run after
/// <c>UseAuthentication()</c> — it does in all hosts, via <c>UseOrderSphereRequestLogging()</c>.
/// </para>
/// <para>
/// <c>tenant_id</c>, <c>correlation_id</c> and <c>user_id</c> reach the log record through
/// <see cref="OrderSphereLogEnricher"/> rather than this scope, so that worker logs carry the
/// same fields without an <c>HttpContext</c>.
/// </para>
/// </summary>
internal sealed class RequestContextEnrichmentMiddleware(
    RequestDelegate next,
    ILogger<RequestContextEnrichmentMiddleware> logger)
{
    internal const string CorrelationHeader = "X-Request-Id";

    /// <summary>
    /// Where <see cref="OrderSphereLogEnricher"/> looks when the ambient scopes are already gone.
    /// <para>
    /// Every host registers <c>UseExceptionHandler()</c> ahead of <c>UseOrderSphereRequestLogging()</c>,
    /// which it must: the handler can only catch what runs inside it. The consequence is that an
    /// unhandled exception unwinds past this middleware — disposing both <c>AsyncLocal</c> scopes —
    /// before <c>ExceptionHandlerMiddleware</c> logs it, so the one record an operator most wants to
    /// correlate would be the only one without a <c>correlation_id</c>. The <see cref="HttpContext"/>
    /// itself outlives the scopes and is the same instance in both middlewares, so stashing the
    /// values on it closes the gap without reordering the pipeline in ten hosts.
    /// </para>
    /// </summary>
    internal const string CorrelationItemKey = "OrderSphere.CorrelationId";

    /// <inheritdoc cref="CorrelationItemKey"/>
    internal const string TenantItemKey = "OrderSphere.TenantId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        using var correlationScope = AmbientCorrelationContext.BeginScope(correlationId);
        context.Items[CorrelationItemKey] = correlationId;

        // Echo it back so a client (or the browser dev tools) can quote the id in a bug report.
        // Deferred to OnStarting rather than assigned outright: in the two proxying hosts
        // (ApiGateway, Bff) YARP copies the downstream response headers on top of this one and the
        // proxied service echoes the very same id, so a plain assignment reaches the client as
        // "id,id". OnStarting runs after that copy, immediately before the headers are flushed.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = correlationId;
            return Task.CompletedTask;
        });

        // No org_id (anonymous traffic, or a deployment with Auth0 Organizations not enabled)
        // opens no scope at all, leaving tenant_id off the record rather than stamping an
        // all-zero GUID that reads like a real tenant. ITenantContext still resolves
        // TenantId.Default for persistence, so EF behaviour is unchanged either way.
        var tenantId = TenantClaimResolver.Resolve(context.User);
        using IDisposable? tenantScope = tenantId is { } resolvedTenant
            ? AmbientTenantContext.BeginScope(resolvedTenant)
            : null;

        if (tenantId is { } stashedTenant)
        {
            context.Items[TenantItemKey] = stashedTenant;
        }

        // The raw client IP is personal data under GDPR and would otherwise sit on every single
        // record. A truncated keyed hash keeps per-client grouping (rate-limit abuse, error
        // clustering) without storing the address itself.
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["client_ip_hash"] = HashClientIp(ResolveClientIp(context)),
        }))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationHeader, out var header)
            && !string.IsNullOrWhiteSpace(header))
        {
            return header.ToString();
        }

        return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }

    private static string ResolveClientIp(HttpContext context)
    {
        // X-Forwarded-For takes precedence when running behind a reverse proxy / gateway.
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded)
            && !string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.ToString().Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "-";
    }

    private static string HashClientIp(string clientIp)
    {
        if (clientIp == "-")
        {
            return clientIp;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientIp));
        return Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }
}
