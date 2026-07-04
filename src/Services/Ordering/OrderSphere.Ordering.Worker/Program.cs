using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.Behaviors;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;
using OrderSphere.Ordering.Application.Abstractions;
using OrderSphere.Ordering.Infrastructure;
using OrderSphere.Ordering.Infrastructure.CatalogClient;
using OrderSphere.Ordering.Infrastructure.Persistence;
using OrderSphere.Ordering.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Azure Service Bus
builder.AddAzureServiceBusClient("azure-service-bus");

// EventBus abstraction
builder.Services.AddAzureServiceBusEventBus();

// Redis distributed lock — must precede AddOrderingOutboxProcessing so the
// Redis implementation takes precedence over the NullDistributedLock fallback.
await builder.AddOrderSphereRedisAsync();
builder.Services.AddOrderSphereDistributedLocking();

// EF Core — Aspire injects connection string via "ordering-db".
// Not pooled: OrderingDbContext takes scoped services (ICurrentUser, ITenantContext), which a
// DbContext pool cannot resolve. Enrich re-adds Aspire's retry, health-check and telemetry
// wiring on top of the plain registration.
builder.Services.AddDbContext<OrderingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ordering-db")));
builder.EnrichNpgsqlDbContext<OrderingDbContext>(settings =>
{
    settings.DisableRetry = false;
});

// Ordering infrastructure (outbox handler registrations, DI bindings)
builder.Services.AddOrderingInfrastructure(builder.Environment);
builder.Services.AddOrderingOutboxProcessing();

// HTTP client for Catalog service — confirm/release stock reservations on payment result.
builder.Services.AddHttpClient<ICatalogClient, HttpCatalogClient>(client =>
{
    var catalogUrl = builder.Configuration["Services:Catalog:BaseUrl"]
        ?? "https://ordersphere-catalog";
    client.BaseAddress = new Uri(catalogUrl);
})
    .AddClientCredentialsHandler()
    // No-op unless "Chaos:Enabled" is set — used for resilience drills / chaos testing.
    .AddOrderSphereChaos(builder.Configuration);

// Service Bus consumers
builder.Services.AddHostedService<OrderProcessor>();
builder.Services.AddHostedService<PaymentResultProcessor>();
builder.Services.AddHostedService<PaymentRefundProcessor>();
builder.Services.AddHostedService<OrderHistoryProjector>();
builder.Services.AddHostedService<CustomerErasureProcessor>();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
builder.Services.AddTransient(typeof(INotificationHandler<>), typeof(DomainEventLoggingHandler<>));

// DLQ admin surface: admin-protected dead-letter reader/replay for this worker's queues, plus the
// ordersphere.dlq.depth gauge. JWT auth mirrors the API services (Oidc config is already injected).
builder.AddOrderSphereJwtAuth("ordering-worker");
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));
builder.Services.AddDlqAdmin("orders", "payment-results", "payment-refunds", "order-history", "erasure-ordering");

// D2 — queryable audit trail: admin-protected read of AuditLogEntry rows written by OrderingDbContext.
builder.Services.AddScoped<IAuditLogQuery, EfAuditLogQuery<OrderingDbContext>>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Admin DLQ surface — the gateway forwards /api/v1/admin/ordering/dlq/** here.
app.MapDlqAdminEndpoints("api/v1/admin/ordering/dlq", "AdminPolicy");

// Admin audit-log surface — the gateway forwards /api/v1/admin/ordering/audit-log/** here.
app.MapAuditLogAdminEndpoints("api/v1/admin/ordering/audit-log", "AdminPolicy");

// Liveness/readiness endpoints (/health, /alive, /version) for container probes.
app.MapDefaultEndpoints();

app.Run();
