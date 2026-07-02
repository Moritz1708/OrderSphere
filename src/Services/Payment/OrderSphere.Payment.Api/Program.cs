using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.Payment.Api.Configuration;
using OrderSphere.Payment.Api.Endpoints;
using OrderSphere.Payment.Application;
using OrderSphere.Payment.Infrastructure;
using OrderSphere.Payment.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderSphereSwagger("OrderSphere Payment API");

builder.AddNpgsqlDbContext<PaymentDbContext>("payment-db", settings =>
{
    settings.DisableRetry = false;
});

builder.AddAzureServiceBusClient("azure-service-bus");
builder.Services.AddAzureServiceBusEventBus();

// Redis distributed lock — must precede AddPaymentInfrastructure (which calls AddOutboxProcessing)
// so the Redis implementation takes precedence over the NullDistributedLock fallback.
await builder.AddOrderSphereRedisAsync();
builder.Services.AddOrderSphereDistributedLocking();

builder.Services.AddPaymentInfrastructure(builder.Configuration);

builder.Services.AddPaymentApplication();

var paymentConnectionString = builder.Configuration.GetConnectionString("payment-db") ?? "";
builder.Services.AddHealthChecks()
    .AddNpgSql(paymentConnectionString, name: "postgres");

builder.AddOrderSphereExceptionHandling();
builder.Services.AddPaymentApiVersioning();

builder.AddOrderSphereJwtAuth("payment-api");
builder.Services.AddCurrentUser();
builder.Services.AddTenantContext();

var app = builder.Build();

// Skipped under "Testing": integration tests boot with a non-relational in-memory provider
// where the relational Migrate() would throw. See OrderSphere.IntegrationTests.Api.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseOrderSphereSwagger(docTitle: "OrderSphere Payment API");
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrderSphereRequestLogging();

app.MapPaymentEndpoints();
app.MapInternalPaymentEndpoints();
app.MapStripeWebhookEndpoints();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapDefaultEndpoints();

app.Run();
