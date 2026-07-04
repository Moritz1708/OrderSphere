using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.Invoicing.Infrastructure.Pdf;
using OrderSphere.Invoicing.Infrastructure.Persistence;

namespace OrderSphere.Invoicing.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInvoicingInfrastructure(this IHostApplicationBuilder builder)
    {
        // Not pooled: InvoicingDbContext takes scoped services (ICurrentUser, ITenantContext),
        // which a DbContext pool cannot resolve. Enrich re-adds Aspire's retry, health-check
        // and telemetry wiring on top of the plain registration.
        builder.Services.AddDbContext<InvoicingDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("invoicing-db")));
        builder.EnrichNpgsqlDbContext<InvoicingDbContext>();

        builder.Services.AddScoped<IInvoicingDbContext>(sp =>
            sp.GetRequiredService<InvoicingDbContext>());

        builder.Services.AddScoped<IInvoicePdfService, QuestPdfInvoiceService>();

        builder.Services.AddScoped<IInvoiceNumberGenerator, SequentialInvoiceNumberGenerator>();

        builder.Services.Configure<InvoicingOptions>(builder.Configuration.GetSection(InvoicingOptions.SectionName));
        builder.Services.AddScoped<IInvoiceTaxRateProvider, ConfiguredInvoiceTaxRateProvider>();

        builder.Services.AddSingleton(sp => new BlobStorageClients(
            sp.GetRequiredService<IConfiguration>(), "InvoiceBlob:Endpoint", "invoices", "invoices"));

        builder.Services.AddScoped<IBlobStorageService>(sp =>
        {
            var clients = sp.GetRequiredService<BlobStorageClients>();
            return clients.IsEnabled
                ? new AzureBlobStorageService(
                    clients,
                    sp.GetRequiredService<ILogger<AzureBlobStorageService>>())
                : DisabledBlobStorageService.Instance;
        });

        builder.Services.AddAzureServiceBusEventBus();

        return builder;
    }
}
