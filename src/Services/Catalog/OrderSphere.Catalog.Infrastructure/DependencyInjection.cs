using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderSphere.Catalog.Infrastructure.Persistence;
using OrderSphere.Catalog.Infrastructure.Search;

namespace OrderSphere.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddCatalogInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: CatalogDbContext takes scoped services (ICurrentUser, ITenantContext),
        // which a DbContext pool cannot resolve. Enrich re-adds Aspire's retry, health-check
        // and telemetry wiring on top of the plain registration.
        builder.Services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("catalog-db")));
        builder.EnrichNpgsqlDbContext<CatalogDbContext>(settings =>
        {
            settings.DisableRetry = false;
        });

        builder.Services.AddScoped<ICatalogDbContext>(sp =>
            sp.GetRequiredService<CatalogDbContext>());

        // The Redis L2 (IDistributedCache) is registered by the API host via
        // AddOrderSphereRedisAsync, which adds Entra ID authentication for Azure Managed Redis.
        builder.Services.AddHybridCache();

        // Azure AI Search: the search/embedding clients are built once (singleton); the
        // scoped index borrows them and reads the catalog. When Search/Foundry are not
        // configured, a no-op index is used and queries fall back to database search.
        builder.Services.AddSingleton<ProductSearchClients>();
        builder.Services.AddScoped<IProductSearchIndex>(sp =>
        {
            var clients = sp.GetRequiredService<ProductSearchClients>();
            return clients.IsEnabled
                ? new AzureAiProductSearchIndex(
                    sp.GetRequiredService<ICatalogDbContext>(),
                    clients,
                    sp.GetRequiredService<ILogger<AzureAiProductSearchIndex>>())
                : DisabledProductSearchIndex.Instance;
        });

        // Azure Blob Storage: the blob client is built once (singleton); the scoped service
        // borrows it. When Blob/images is not configured, a no-op service is used and
        // uploaded images fall back to external URL behaviour (graceful degradation).
        builder.Services.AddSingleton(sp => new BlobStorageClients(
            sp.GetRequiredService<IConfiguration>(), "Blob:Endpoint", "images", "product-images"));
        builder.Services.AddScoped<IBlobStorageService>(sp =>
        {
            var clients = sp.GetRequiredService<BlobStorageClients>();
            return clients.IsEnabled
                ? new AzureBlobStorageService(
                    clients,
                    sp.GetRequiredService<ILogger<AzureBlobStorageService>>())
                : DisabledBlobStorageService.Instance;
        });

        return builder;
    }
}
