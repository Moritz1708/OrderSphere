using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OrderSphere.Partners.Api.Configuration;
using OrderSphere.Partners.Api.Endpoints;
using OrderSphere.Partners.Application;
using OrderSphere.Partners.Infrastructure;
using OrderSphere.Partners.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddOrderSphereSwagger("OrderSphere Partners API");

builder.AddPartnersInfrastructure();
builder.Services.AddPartnersApplication();

var partnersConnectionString = builder.Configuration.GetConnectionString("partners-db") ?? "";
builder.Services.AddHealthChecks()
    .AddNpgSql(partnersConnectionString, name: "postgres");

builder.AddOrderSphereExceptionHandling();
builder.Services.AddPartnersApiVersioning();

builder.AddOrderSphereJwtAuth("partners-api");
builder.Services.AddTenantContext();
builder.Services.AddPartnersAuthorization();

var app = builder.Build();

// Integration tests boot the host with an in-memory provider and supply the schema
// themselves; the relational Migrate() is a no-op there and would throw on a non-relational
// provider, so it is skipped under the "Testing" environment.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PartnersDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseOrderSphereSwagger(docTitle: "OrderSphere Partners API");
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrderSphereRequestLogging();

app.MapPartnersEndpoints();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapDefaultEndpoints();

app.Run();
