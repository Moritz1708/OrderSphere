using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using OrderSphere.ApiGateway.Authentication;

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
        context.Request.Headers["X-Request-Id"] = Guid.NewGuid().ToString("N");
    }
    context.Response.Headers["X-Request-Id"] = context.Request.Headers["X-Request-Id"].ToString();
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
