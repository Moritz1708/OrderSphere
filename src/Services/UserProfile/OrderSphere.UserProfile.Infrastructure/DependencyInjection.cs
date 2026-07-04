using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.Outbox;
using OrderSphere.UserProfile.Application.Abstractions;
using OrderSphere.UserProfile.Infrastructure.Outbox;
using OrderSphere.UserProfile.Infrastructure.Persistence;

namespace OrderSphere.UserProfile.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddUserProfileInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: UserProfileDbContext takes scoped services (ICurrentUser, ITenantContext),
        // which a DbContext pool cannot resolve. Enrich re-adds Aspire's retry, health-check
        // and telemetry wiring on top of the plain registration.
        builder.Services.AddDbContext<UserProfileDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("userprofile-db")));
        builder.EnrichNpgsqlDbContext<UserProfileDbContext>(settings =>
        {
            settings.DisableRetry = false;
        });

        builder.Services.AddScoped<IUserProfileDbContext>(sp =>
            sp.GetRequiredService<UserProfileDbContext>());

        // Outbox: writes to DB, dispatched by OutboxDispatcher background service.
        builder.Services.AddScoped<IOutboxEventHandler, CustomerErasureRequestedEventHandler>();

        return builder;
    }

    /// <summary>
    /// Registers OutboxDispatcher and OutboxCleanupService as hosted background services.
    /// </summary>
    public static IServiceCollection AddUserProfileOutboxProcessing(this IServiceCollection services)
        => services.AddOutboxProcessing<UserProfileDbContext>();
}
