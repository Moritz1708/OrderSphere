using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.Catalog.Api.BackgroundServices;
using OrderSphere.Catalog.Api.Configuration;
using OrderSphere.Catalog.Api.Endpoints;
using OrderSphere.Catalog.Application;
using OrderSphere.Catalog.Application.Abstractions;
using OrderSphere.Catalog.Infrastructure;
using OrderSphere.Catalog.Infrastructure.OrderingClient;
using OrderSphere.Catalog.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Aspire defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Redis distributed cache (Entra ID auth against Azure Managed Redis) — L2 for HybridCache.
var redisMultiplexer = await builder.AddOrderSphereRedisAsync();
// Leader-election lock so ReservationSweeper runs on exactly one Catalog replica.
builder.Services.AddOrderSphereDistributedLocking();

// Domain layers
builder.AddCatalogInfrastructure();          // EF Core, ICatalogDbContext, HybridCache
builder.Services.AddCatalogApplication();    // MediatR + Behaviors + FluentValidation

// HTTP client for Ordering service (review purchase-verification).
// D4 — Ordering's /internal endpoints now require a valid client-credentials token;
// ClientCredentialsTokenHandler acquires one using Catalog's own Oidc:ClientId/ClientSecret.
builder.Services.AddHttpClient<IOrderingClient, HttpOrderingClient>(client =>
{
    var orderingUrl = builder.Configuration["Services:Ordering:BaseUrl"]
        ?? "https://ordersphere-ordering";
    client.BaseAddress = new Uri(orderingUrl);
}).AddClientCredentialsHandler();

// Cross-cutting concerns
builder.Services.AddCatalogApiVersioning();
builder.Services.AddCatalogSwagger();
builder.Services.AddCatalogRateLimiting(redisMultiplexer);
builder.AddCatalogAuthentication();     // Auth0 JWT; audience "catalog-api"
builder.Services.AddCatalogAuthorization();                          // CatalogAdminPolicy
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();

// D2 — queryable audit trail: admin-protected read of AuditLogEntry rows written by CatalogDbContext.
builder.Services.AddScoped<IAuditLogQuery, EfAuditLogQuery<CatalogDbContext>>();

// Exception handling
builder.AddOrderSphereExceptionHandling();

// Health checks (Postgres)
var catalogConnectionString = builder.Configuration.GetConnectionString("catalog-db") ?? "";
builder.Services.AddHealthChecks()
    .AddNpgSql(catalogConnectionString, name: "postgres");

// gRPC
builder.Services.AddGrpc();

// Background release of expired stock reservations (saga TTL compensation).
builder.Services.AddScheduledJob<ReservationSweeper>();

// Ensures the Azure AI Search index exists and seeds it when empty (no-op if unconfigured).
builder.Services.AddHostedService<CatalogSearchInitializer>();

// Ensures the product-images blob container exists (no-op if blob storage is not configured).
builder.Services.AddHostedService<BlobContainerInitializer>();

// Seeds brands, categories and products from embedded data (Development by default).
// Registered last so the search index and blob container are initialized first.
builder.Services.AddHostedService<CatalogDataSeeder>();

var app = builder.Build();

// Skipped under "Testing": integration tests boot with an in-memory relational provider and
// create the schema themselves; the Npgsql Migrate() would not apply. See OrderSphere.IntegrationTests.Api.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseCatalogSwagger();
}

// Middleware pipeline (order matters)
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrderSphereRequestLogging();

// Endpoints
app.MapCatalogEndpoints();

// Admin audit-log surface — the gateway forwards /api/v1/admin/catalog/audit-log/** here.
app.MapAuditLogAdminEndpoints("api/v1/admin/catalog/audit-log", AuthorizationExtensions.CatalogAdminPolicy);

// Health
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapDefaultEndpoints();

app.Run();
