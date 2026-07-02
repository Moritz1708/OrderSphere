using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.Auditing;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.UserProfile.Api.Configuration;
using OrderSphere.UserProfile.Api.Endpoints;
using OrderSphere.UserProfile.Application;
using OrderSphere.UserProfile.Infrastructure;
using OrderSphere.UserProfile.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderSphereSwagger("OrderSphere UserProfile API");

// Domain layers
builder.AddUserProfileInfrastructure();        // EF Core, IUserProfileDbContext
builder.Services.AddUserProfileApplication();  // MediatR + Behaviors + FluentValidation

// Outbox → Service Bus (D1: publishes CustomerErasureRequestedIntegrationEvent fan-out).
builder.Services.AddUserProfileOutboxProcessing();
builder.AddAzureServiceBusClient("azure-service-bus");
builder.Services.AddAzureServiceBusEventBus();

// JWT Bearer — shared Auth0 validation; audience "userprofile-api" is a
// dedicated API registered in the Auth0 tenant.
builder.AddOrderSphereJwtAuth("userprofile-api");
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();

builder.AddOrderSphereExceptionHandling();
builder.Services.AddUserProfileApiVersioning();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));

// D2 — queryable audit trail: admin-protected read of AuditLogEntry rows written by UserProfileDbContext.
builder.Services.AddScoped<IAuditLogQuery, EfAuditLogQuery<UserProfileDbContext>>();

var app = builder.Build();

// Skipped under "Testing": integration tests boot with a non-relational in-memory provider
// where the relational Migrate() would throw. See OrderSphere.IntegrationTests.Api.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<UserProfileDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseOrderSphereSwagger(docTitle: "OrderSphere UserProfile API");
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrderSphereRequestLogging();

app.MapUserProfileEndpoints();

// Admin audit-log surface — the gateway forwards /api/v1/admin/userprofile/audit-log/** here.
app.MapAuditLogAdminEndpoints("api/v1/admin/userprofile/audit-log", "AdminPolicy");

app.MapDefaultEndpoints();

app.Run();
