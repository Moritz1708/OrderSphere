using Microsoft.AspNetCore.Builder;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;
using OrderSphere.Webhooks.Application;
using OrderSphere.Webhooks.Infrastructure;
using OrderSphere.Webhooks.Worker.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Redis distributed lock — guards WebhookDeliveryProcessor against double-delivery at scale.
await builder.AddOrderSphereRedisAsync();
builder.Services.AddOrderSphereDistributedLocking();

builder.AddWebhooksInfrastructure();
builder.Services.AddWebhooksApplication();

builder.AddAzureServiceBusClient("azure-service-bus");

builder.Services.AddHttpClient("WebhookDelivery", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", "OrderSphere-Webhooks/1.0");
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

// Admin DLQ surface — the gateway forwards /api/v1/admin/webhooks/dlq/** here.
app.MapDlqAdminEndpoints("api/v1/admin/webhooks/dlq", "AdminPolicy");

// Liveness/readiness endpoints (/health, /alive, /version) for container probes.
app.MapDefaultEndpoints();

app.Run();
