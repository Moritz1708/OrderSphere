using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using OrderSphere.ApiGateway.Authentication;
using OrderSphere.BuildingBlocks.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddOrderSphereJwtAuth();

// B6 — B2B partners authenticate with X-API-Key instead of a user JWT. A policy scheme
// picks the concrete scheme per request so existing JWT-authenticated routes are unaffected;
// only routes with the "partner" policy accept the ApiKey scheme.
const string PolicySchemeName = "smart";

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = PolicySchemeName;
    options.DefaultChallengeScheme = PolicySchemeName;
})
    .AddPolicyScheme(PolicySchemeName, "JWT or API Key", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                ? ApiKeyAuthenticationHandler.SchemeName
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddMemoryCache();

// D4 — the gateway's own M2M identity, used to resolve X-API-Key headers against the
// Partners service's internal lookup endpoint.
builder.Services.AddHttpClient<IPartnerLookupClient, HttpPartnerLookupClient>(client =>
    client.BaseAddress = new Uri("https://ordersphere-partners"))
    .AddServiceDiscovery()
    .AddClientCredentialsHandler();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("partner", policy => policy
        .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser());
});

// D3 — distributed rate-limiting: gateway limiters share their quota counters across
// every gateway instance via Redis instead of counting in-process.
var redisMultiplexer = await builder.AddOrderSphereRedisAsync();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Named limiters available for endpoint-level policies via [EnableRateLimiting].
    options.AddPolicy("gateway-global", _ =>
        RedisRateLimitPartition.GetRedisFixedWindowLimiter(
            "gateway-global", redisMultiplexer, permitLimit: 200, window: TimeSpan.FromMinutes(1)));

    options.AddPolicy("gateway-authenticated", _ =>
        RedisRateLimitPartition.GetRedisFixedWindowLimiter(
            "gateway-authenticated", redisMultiplexer, permitLimit: 100, window: TimeSpan.FromMinutes(1)));

    // Global limiter runs after UseAuthentication() so the sub/partner claims are available.
    // Authenticated users are partitioned per user-id (120 req/min) to prevent a
    // compromised token from consuming the IP quota of other users on shared egress
    // (NAT, corporate proxies). Anonymous callers fall back to per-IP (30 req/min).
    // B6 — partners (X-API-Key) are partitioned per partner-id instead, with a quota that
    // depends on their tier (Standard/Premium) rather than the flat per-user limit.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partnerId = context.User.FindFirst("partner_id")?.Value;
        if (partnerId is not null)
        {
            var quotaTier = context.User.FindFirst("quota_tier")?.Value;
            var permitLimit = quotaTier == "Premium" ? 300 : 60;
            return RedisRateLimitPartition.GetRedisFixedWindowLimiter(
                $"partner:{partnerId}", redisMultiplexer, permitLimit: permitLimit, window: TimeSpan.FromMinutes(1));
        }

        var sub = context.User.FindFirst("sub")?.Value;
        if (sub is not null)
        {
            return RedisRateLimitPartition.GetRedisFixedWindowLimiter(
                $"user:{sub}", redisMultiplexer, permitLimit: 120, window: TimeSpan.FromMinutes(1));
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RedisRateLimitPartition.GetRedisFixedWindowLimiter(
            $"ip:{clientIp}", redisMultiplexer, permitLimit: 30, window: TimeSpan.FromMinutes(1));
    });
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Use(async (context, next) =>
{
    if (!context.Request.Headers.ContainsKey("X-Request-Id"))
    {
        // Seeded from the trace id so the client-visible id, the log correlation_id and the
        // trace are one and the same value. A client may still supply its own id, which is why
        // the outbox persists the correlation id in its own column rather than deriving it from
        // the trace. Same 32-char lowercase hex shape as the previous Guid("N").
        context.Request.Headers["X-Request-Id"] =
            Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }

    var correlationId = context.Request.Headers["X-Request-Id"].ToString();

    // On OnStarting rather than assigned here: MapReverseProxy copies the proxied service's
    // response headers over this one, and that service echoes the same id, so a plain assignment
    // would reach the client as "id,id". OnStarting runs after the copy, just before the flush.
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Request-Id"] = correlationId;
        return Task.CompletedTask;
    });

    // Opened here, not left to UseOrderSphereRequestLogging further down the pipeline: the
    // authentication and rate-limiting middleware between the two emit the 401/403 audit records
    // and the 429s, and those were the gateway's only records with no correlation_id — despite
    // the id already sitting in the request headers at that point.
    using var correlationScope = AmbientCorrelationContext.BeginScope(correlationId);

    await next();
});

// Authentication before rate limiting so the GlobalLimiter can partition on the sub claim.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOrderSphereRequestLogging();

app.MapReverseProxy();
app.MapHealthChecks("/health/gateway");

app.Run();
