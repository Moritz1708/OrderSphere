using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderSphere.Advisory.Application.Abstractions;
using OrderSphere.Advisory.Infrastructure.Cleanup;
using OrderSphere.Advisory.Infrastructure.Persistence;

namespace OrderSphere.Advisory.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddAdvisoryInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: AdvisoryDbContext takes scoped services (ICurrentUser, ITenantContext),
        // which a DbContext pool cannot resolve. Enrich re-adds Aspire's retry, health-check
        // and telemetry wiring on top of the plain registration.
        builder.Services.AddDbContext<AdvisoryDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("advisory-db")));
        builder.EnrichNpgsqlDbContext<AdvisoryDbContext>(settings =>
        {
            settings.DisableRetry = false;
        });

        builder.Services.AddScoped<IAdvisoryDbContext>(sp =>
            sp.GetRequiredService<AdvisoryDbContext>());

        builder.Services.AddHostedService<ConversationCleanupService>();

        return builder;
    }
}
