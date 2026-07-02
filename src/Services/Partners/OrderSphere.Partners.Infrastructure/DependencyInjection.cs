using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderSphere.Partners.Application.Abstractions;
using OrderSphere.Partners.Infrastructure.Persistence;

namespace OrderSphere.Partners.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddPartnersInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<PartnersDbContext>("partners-db", settings =>
        {
            settings.DisableRetry = false;
        });

        builder.Services.AddScoped<IPartnersDbContext>(sp =>
            sp.GetRequiredService<PartnersDbContext>());

        return builder;
    }
}
