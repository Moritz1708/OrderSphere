using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.Security;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Establishes the ambient correlation id for an HTTP request and pushes the request-local
/// fields that are not process-wide into the log scope.
/// <para>
/// The correlation id is taken from the <c>X-Request-Id</c> header the API Gateway sets
/// (<c>ApiGateway/Program.cs</c>), so a value chosen at the edge survives every downstream hop.
/// Services reached directly fall back to the current trace id, and only mint a new id when
/// there is neither.
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

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        // Echo it back so a client (or the browser dev tools) can quote the id in a bug report.
        context.Response.Headers[CorrelationHeader] = correlationId;

        using var correlationScope = AmbientCorrelationContext.BeginScope(correlationId);

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
