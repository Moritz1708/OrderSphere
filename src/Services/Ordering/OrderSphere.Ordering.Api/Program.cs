using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Behaviors;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;
using OrderSphere.Ordering.Api.Authorization;
using OrderSphere.Ordering.Api.Configuration;
using OrderSphere.Ordering.Api.Endpoints;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Infrastructure;
using OrderSphere.Ordering.Infrastructure.CatalogClient;
using OrderSphere.Ordering.Infrastructure.Idempotency;
using OrderSphere.Ordering.Infrastructure.Persistence;
using HttpBasketClient = OrderSphere.Ordering.Infrastructure.CatalogClient.HttpBasketClient;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderSphereSwagger("OrderSphere Ordering API");

// EF Core — Aspire injects connection string via "ordering-db"
builder.AddNpgsqlDbContext<OrderingDbContext>("ordering-db", settings =>
{
    settings.DisableRetry = false;
});

// Ordering Infrastructure
builder.Services.AddOrderingInfrastructure(builder.Environment);
builder.Services.AddOrderingOutboxProcessing();
// Azure Service Bus (for OutboxDispatcher → RealServiceBusPublisher)
builder.AddAzureServiceBusClient("azure-service-bus");

// EventBus abstraction
builder.Services.AddAzureServiceBusEventBus();

// Distributed cache (Redis) for checkout idempotency key deduplication (30-min TTL per key).
// Redis-backed so the guard holds across multiple Ordering.Api instances.
await builder.AddOrderSphereRedisAsync();
builder.Services.AddOrderSphereDistributedLocking();
builder.Services.AddScoped<ICheckoutIdempotencyStore, RedisCheckoutIdempotencyStore>();

// Retention cleanup: processed inbox rows and audit log entries past their retention window.
builder.Services.AddScheduledJob<InboxCleanupJob<OrderingDbContext>>();
builder.Services.AddScheduledJob<AuditLogRetentionJob<OrderingDbContext>>();

// MediatR — scan this assembly for handlers + pipeline behaviors
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(IOrderingDbContext).Assembly);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddTransient(typeof(INotificationHandler<>), typeof(DomainEventLoggingHandler<>));

// FluentValidation — scan this assembly for validators
builder.Services.AddValidatorsFromAssembly(typeof(IOrderingDbContext).Assembly);

// HTTP client for Catalog service (internal stock operations).
// ClientCredentialsTokenHandler acquires a client_credentials token (configured
// via Oidc:ClientId / Oidc:ClientSecret) and forwards it as a Bearer token.
// Note: AddStandardResilienceHandler is applied globally via AddServiceDefaults().
builder.Services.AddHttpClient<ICatalogClient, HttpCatalogClient>(client =>
{
    var catalogUrl = builder.Configuration["Services:Catalog:BaseUrl"]
        ?? "https://ordersphere-catalog";
    client.BaseAddress = new Uri(catalogUrl);
}).AddClientCredentialsHandler();

// HTTP client for Basket service (cart read/clear during checkout).
builder.Services.AddHttpClient<IBasketClient, HttpBasketClient>(client =>
{
    var basketUrl = builder.Configuration["Services:Basket:BaseUrl"]
        ?? "https://ordersphere-basket";
    client.BaseAddress = new Uri(basketUrl);
}).AddClientCredentialsHandler();

// Health checks
var orderingConnectionString = builder.Configuration.GetConnectionString("ordering-db") ?? "";
builder.Services.AddHealthChecks()
    .AddNpgSql(orderingConnectionString, name: "postgres");

// Validation exception → HTTP 400
builder.AddOrderSphereExceptionHandling();
builder.Services.AddOrderingApiVersioning();

// JWT Bearer — shared Auth0 validation; audience "ordering-api" is a
// dedicated API registered in the Auth0 tenant.
builder.AddOrderSphereJwtAuth("ordering-api");
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.Admin,
        p => p.RequireRole("admin"))
    .AddPolicy(AuthorizationPolicies.Staff,
        p => p.RequireRole("csr", "order-manager", "admin"))
    .AddPolicy(AuthorizationPolicies.OrderManager,
        p => p.RequireRole("order-manager", "admin"))
    .AddPolicy(AuthorizationPolicies.OrderOwnerOrStaff,
        p => p.AddRequirements(new OrderOwnerOrStaffRequirement()));

// ABAC handler — registered as singleton (stateless, no scoped dependencies).
builder.Services.AddSingleton<IAuthorizationHandler, OrderOwnerOrStaffHandler>();

var app = builder.Build();

// Skipped under "Testing": integration tests boot with a non-relational in-memory provider
// where the relational Migrate() would throw. See OrderSphere.IntegrationTests.Api.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseOrderSphereSwagger(docTitle: "OrderSphere Ordering API");
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrderSphereRequestLogging();

app.MapOrderingEndpoints();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapDefaultEndpoints();

app.Run();
