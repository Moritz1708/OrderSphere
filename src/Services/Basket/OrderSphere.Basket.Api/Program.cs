using Microsoft.EntityFrameworkCore;
using OrderSphere.Basket.Api.Configuration;
using OrderSphere.Basket.Api.Endpoints;
using OrderSphere.Basket.Application;
using OrderSphere.Basket.Infrastructure;
using OrderSphere.Basket.Infrastructure.Persistence;
using OrderSphere.BuildingBlocks.Auditing;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderSphereSwagger("OrderSphere Basket API");

// D3 — distributed rate-limiting: shared Redis-backed quota counters (RateLimitingExtensions).
var redisMultiplexer = await builder.AddOrderSphereRedisAsync();

builder.AddBasketInfrastructure();
builder.Services.AddBasketApplication();

// gRPC client for Catalog service (internal stock checks). Internal Catalog endpoints are
// network-protected (no auth), so no client-credentials handler is attached.
builder.AddCatalogGrpcClient();

// Health checks
var basketConnectionString = builder.Configuration.GetConnectionString("basket-db") ?? "";
builder.Services.AddHealthChecks()
    .AddNpgSql(basketConnectionString, name: "postgres");

builder.Services.AddBasketApiVersioning();
builder.Services.AddBasketRateLimiting(redisMultiplexer);

// Validation exception → HTTP 400
builder.AddOrderSphereExceptionHandling();

// JWT Bearer
builder.AddOrderSphereJwtAuth("basket-api");
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));

// D2 — queryable audit trail: admin-protected read of AuditLogEntry rows written by BasketDbContext.
builder.Services.AddScoped<IAuditLogQuery, EfAuditLogQuery<BasketDbContext>>();

var app = builder.Build();

// Skipped under "Testing": integration tests boot with a non-relational in-memory provider
// where the relational Migrate() would throw. See OrderSphere.IntegrationTests.Api.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<BasketDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseOrderSphereSwagger(docTitle: "OrderSphere Basket API");
}

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrderSphereRequestLogging();

app.MapCartEndpoints();
app.MapInternalCartEndpoints();

// Admin audit-log surface — the gateway forwards /api/v1/admin/basket/audit-log/** here.
app.MapAuditLogAdminEndpoints("api/v1/admin/basket/audit-log", "AdminPolicy");

// Health endpoints (/health, /alive) are mapped by MapDefaultEndpoints from ServiceDefaults.
// Do not map "/health" again here — a second registration makes the route ambiguous and every
// probe returns 500 (AmbiguousMatchException).
app.MapDefaultEndpoints();

app.Run();
