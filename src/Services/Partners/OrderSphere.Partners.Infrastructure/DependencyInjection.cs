using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderSphere.Partners.Application.Abstractions;
using OrderSphere.Partners.Infrastructure.Persistence;

namespace OrderSphere.Partners.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddPartnersInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: PartnersDbContext takes a scoped ITenantContext, which a DbContext pool
        // cannot resolve. Enrich re-adds Aspire's retry, health-check and telemetry wiring on
        // top of the plain registration.
        builder.Services.AddDbContext<PartnersDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("partners-db")));
        builder.EnrichNpgsqlDbContext<PartnersDbContext>(settings =>
        {
            settings.DisableRetry = false;
        });

        builder.Services.AddScoped<IPartnersDbContext>(sp =>
            sp.GetRequiredService<PartnersDbContext>());

        return builder;
    }
}
