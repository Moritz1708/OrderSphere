using Microsoft.AspNetCore.Builder;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Scheduling;
using OrderSphere.Webhooks.Application;
using OrderSphere.Webhooks.Infrastructure;
using OrderSphere.Webhooks.Infrastructure.Persistence;
using OrderSphere.Webhooks.Worker.Delivery;
using OrderSphere.Webhooks.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Redis distributed lock — guards WebhookDeliveryProcessor against double-delivery at scale.
await builder.AddOrderSphereRedisAsync();
builder.Services.AddOrderSphereDistributedLocking();

builder.AddWebhooksInfrastructure();
builder.Services.AddWebhooksApplication();

// Retention cleanup: processed inbox rows past their retention window (Retention:InboxDays, default 30).
// WebhooksDbContext does not track AuditLogEntry, so no AuditLogRetentionJob here.
builder.Services.AddScheduledJob<InboxCleanupJob<WebhooksDbContext>>();

builder.AddAzureServiceBusClient("azure-service-bus");

// Targets are chosen by customers: every connection is checked against the SSRF policy, redirects
// are not followed (a public host could bounce to an internal one), and no proxy is used so the
// check always applies to the real destination.
builder.Services.AddHttpClient("WebhookDelivery", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", "OrderSphere-Webhooks/1.0");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseProxy = false,
    ConnectCallback = WebhookTargetConnector.ConnectAsync,
});

builder.Services.AddHostedService<WebhookEventProcessor>();
builder.Services.AddHostedService<WebhookDeliveryProcessor>();

// DLQ admin surface: admin-protected dead-letter reader/replay for this worker's queue, plus the
// ordersphere.dlq.depth gauge. JWT auth mirrors the API services (Oidc config is already injected).
builder.AddOrderSphereJwtAuth("webhooks-worker");
builder.Services.AddTenantContext();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));
builder.Services.AddDlqAdmin("webhook-events");

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// DLQ admin endpoints are a real HTTP surface; log them like any other.
// Placed after auth so the log scope carries the authenticated user.
app.UseOrderSphereRequestLogging();

// Admin DLQ surface — the gateway forwards /api/v1/admin/webhooks/dlq/** here.
app.MapDlqAdminEndpoints("api/v1/admin/webhooks/dlq", "AdminPolicy");

// Liveness/readiness endpoints (/health, /alive, /version) for container probes.
app.MapDefaultEndpoints();

app.Run();
