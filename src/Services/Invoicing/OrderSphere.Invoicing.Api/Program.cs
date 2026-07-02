using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Dlq;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.Invoicing.Api.Endpoints;
using OrderSphere.Invoicing.Api.Workers;
using OrderSphere.Invoicing.Application;
using OrderSphere.Invoicing.Infrastructure;
using OrderSphere.Invoicing.Infrastructure.Persistence;
using QuestPDF.Infrastructure;

// QuestPDF community licence — free for open-source / non-commercial projects.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderSphereSwagger("OrderSphere Invoicing API");

builder.AddInvoicingInfrastructure();
builder.Services.AddInvoicingApplication();

builder.AddOrderSphereJwtAuth("invoicing-api");
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();

builder.AddOrderSphereExceptionHandling();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));

// Azure Service Bus — the ServiceBusClient is resolved by InvoiceProcessor.
builder.AddAzureServiceBusClient("azure-service-bus");

// Inbox idempotency (backed by InvoicingDbContext).
builder.Services.AddScoped<IInboxStore, EfInboxStore<InvoicingDbContext>>();

builder.Services.AddHostedService<InvoiceProcessor>();
builder.Services.AddHostedService<CustomerErasureProcessor>();

// DLQ admin surface: admin-protected dead-letter reader/replay for the invoice-generation queue,
// plus the ordersphere.dlq.depth gauge. Reuses the AdminPolicy already registered above.
builder.Services.AddDlqAdmin("invoice-generation", "erasure-invoicing");

// D2 — queryable audit trail: admin-protected read of AuditLogEntry rows written by InvoicingDbContext.
builder.Services.AddScoped<IAuditLogQuery, EfAuditLogQuery<InvoicingDbContext>>();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<InvoicingDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
    app.UseOrderSphereSwagger(docTitle: "OrderSphere Invoicing API");

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrderSphereRequestLogging();

app.MapInvoiceEndpoints();

// Admin DLQ surface — the gateway forwards /api/v1/admin/invoicing/dlq/** here.
app.MapDlqAdminEndpoints("api/v1/admin/invoicing/dlq", "AdminPolicy");

// Admin audit-log surface — the gateway forwards /api/v1/admin/invoicing/audit-log/** here.
app.MapAuditLogAdminEndpoints("api/v1/admin/invoicing/audit-log", "AdminPolicy");

app.MapDefaultEndpoints();

app.Run();
