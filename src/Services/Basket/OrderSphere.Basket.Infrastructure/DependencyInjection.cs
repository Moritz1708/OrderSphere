using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderSphere.Basket.Application.Abstractions;
using OrderSphere.Basket.Infrastructure.CatalogClient;
using OrderSphere.Basket.Infrastructure.Persistence;
using OrderSphere.Catalog.V1;

namespace OrderSphere.Basket.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddBasketInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: BasketDbContext takes scoped services (ICurrentUser, ITenantContext),
        // which a DbContext pool cannot resolve. Enrich re-adds Aspire's retry, health-check
        // and telemetry wiring on top of the plain registration.
        builder.Services.AddDbContext<BasketDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("basket-db")));
        builder.EnrichNpgsqlDbContext<BasketDbContext>(settings =>
        {
            settings.DisableRetry = false;
        });

        builder.Services.AddScoped<IBasketDbContext>(sp =>
            sp.GetRequiredService<BasketDbContext>());

        return builder;
    }

    /// <summary>
    /// Registers the gRPC-backed <see cref="ICatalogClient"/> for internal stock checks (A5).
    /// The standard resilience and service-discovery handlers from ServiceDefaults'
    /// <c>ConfigureHttpClientDefaults</c> apply to the generated channel automatically.
    /// D4 — <c>AddGrpcClient</c> returns an <see cref="IHttpClientBuilder"/>, so the same
    /// client-credentials handler used for HTTP clients attaches a Bearer token to the
    /// underlying HTTP/2 channel; Catalog's gRPC service now requires an authenticated caller.
    /// </summary>
    public static IHostApplicationBuilder AddCatalogGrpcClient(this IHostApplicationBuilder builder)
    {
        var catalogUrl = builder.Configuration["Services:Catalog:BaseUrl"]
            ?? "https://ordersphere-catalog";

        builder.Services.AddGrpcClient<CatalogService.CatalogServiceClient>(options =>
        {
            options.Address = new Uri(catalogUrl);
        }).AddClientCredentialsHandler();

        builder.Services.AddScoped<ICatalogClient, GrpcCatalogClient>();

        return builder;
    }
}
