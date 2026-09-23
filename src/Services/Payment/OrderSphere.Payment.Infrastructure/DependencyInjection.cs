using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;
using OrderSphere.BuildingBlocks.EventBus.AzureServiceBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.Inbox;
using OrderSphere.BuildingBlocks.EventBus.Outbox;
using OrderSphere.Payment.Application.Abstractions;
using OrderSphere.Payment.Infrastructure.Outbox;
using OrderSphere.Payment.Infrastructure.Persistence;
using OrderSphere.Payment.Infrastructure.Providers;

namespace OrderSphere.Payment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.AddScoped<IPaymentDbContext>(sp => sp.GetRequiredService<PaymentDbContext>());

        services.AddScoped<IInboxStore, EfInboxStore<PaymentDbContext>>();

        // Outbox
        services.AddScoped<IOutboxEventHandler, PaymentProcessedEventHandler>();
        services.AddScoped<IOutboxEventHandler, PaymentRefundedEventHandler>();
        services.AddOutboxProcessing<PaymentDbContext>();

        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));

        services.AddSingleton<IPaymentProvider, InvoicePaymentProvider>();
        services.AddSingleton<IPaymentProvider, PayPalPaymentProvider>();

        // Stripe handles the "CreditCard" method when an API key is configured; otherwise the
        // simulated provider keeps local development working without external credentials.
        var stripeApiKey = configuration.GetSection(StripeOptions.SectionName)["ApiKey"];
        if (!string.IsNullOrWhiteSpace(stripeApiKey))
        {
            // The SDK default (80 s timeout, 2 retries) lets one payment run far past the 5-minute
            // Service Bus lock renewal. 30 s × 3 attempts keeps create + capture + cancel inside it;
            // the idempotency keys make the SDK's own retries safe.
            services.AddSingleton<Stripe.IStripeClient>(new Stripe.StripeClient(
                stripeApiKey,
                httpClient: new Stripe.SystemNetHttpClient(
                    new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
                    maxNetworkRetries: 2)));
            services.AddSingleton<IPaymentProvider, StripePaymentProvider>();
        }
        else
        {
            services.AddSingleton<IPaymentProvider, CreditCardPaymentProvider>();
        }

        services.AddSingleton<IPaymentProviderFactory, PaymentProviderFactory>();

        return services;
    }
}
