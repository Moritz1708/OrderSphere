using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;
using OrderSphere.Payment.Application;
using OrderSphere.Payment.Infrastructure;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Not pooled: PaymentDbContext takes scoped services (ICurrentUser, ITenantContext), which a
// DbContext pool cannot resolve. Enrich re-adds Aspire's retry, health-check and telemetry
// wiring on top of the plain registration.
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("payment-db")));
builder.EnrichNpgsqlDbContext<PaymentDbContext>();
builder.AddAzureServiceBusClient("azure-service-bus");

builder.Services.AddAzureServiceBusEventBus(); // Required by OutboxDispatcher → PaymentProcessedEventHandler

// Redis distributed lock — must precede AddPaymentInfrastructure (which calls AddOutboxProcessing)
// so the Redis implementation takes precedence over the NullDistributedLock fallback.
await builder.AddOrderSphereRedisAsync();
builder.Services.AddOrderSphereDistributedLocking();

builder.Services.AddPaymentInfrastructure(builder.Configuration);

// Retention cleanup: processed inbox rows and audit log entries past their retention window.
builder.Services.AddScheduledJob<InboxCleanupJob<PaymentDbContext>>();
builder.Services.AddScheduledJob<AuditLogRetentionJob<PaymentDbContext>>();

builder.Services.AddHostedService<PaymentProcessor>();
builder.Services.AddHostedService<OrderConfirmationFailedProcessor>();
builder.Services.AddHostedService<RefundRequestedProcessor>();
builder.Services.AddHostedService<CustomerErasureProcessor>();

builder.Services.AddPaymentApplication();

// DLQ admin surface: admin-protected dead-letter reader/replay for this worker's queues, plus the
// ordersphere.dlq.depth gauge. JWT auth mirrors the API services (Oidc config is already injected).
builder.AddOrderSphereJwtAuth("payment-worker");
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));
builder.Services.AddDlqAdmin("payment-requests", "order-confirmation-failed", "refund-requested", "erasure-payment");

// D2 — queryable audit trail: admin-protected read of AuditLogEntry rows written by PaymentDbContext.
builder.Services.AddScoped<IAuditLogQuery, EfAuditLogQuery<PaymentDbContext>>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Admin DLQ surface — the gateway forwards /api/v1/admin/payment/dlq/** here.
app.MapDlqAdminEndpoints("api/v1/admin/payment/dlq", "AdminPolicy");

// Admin audit-log surface — the gateway forwards /api/v1/admin/payment/audit-log/** here.
app.MapAuditLogAdminEndpoints("api/v1/admin/payment/audit-log", "AdminPolicy");

// Liveness/readiness endpoints (/health, /alive, /version) for container probes.
app.MapDefaultEndpoints();

app.Run();
