using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.Webhooks.Application.Abstractions;
using OrderSphere.Webhooks.Infrastructure.Persistence;

namespace OrderSphere.Webhooks.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddWebhooksInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: WebhooksDbContext takes a scoped ITenantContext, which a DbContext pool
        // cannot resolve. Enrich re-adds Aspire's retry, health-check and telemetry wiring on
        // top of the plain registration.
        builder.Services.AddDbContext<WebhooksDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("webhooks-db")));
        builder.EnrichNpgsqlDbContext<WebhooksDbContext>(settings =>
        {
            settings.DisableRetry = false;
        });

        builder.Services.AddScoped<IWebhooksDbContext>(sp =>
            sp.GetRequiredService<WebhooksDbContext>());

        builder.Services.AddScoped<IInboxStore, EfInboxStore<WebhooksDbContext>>();

        return builder;
    }
}
