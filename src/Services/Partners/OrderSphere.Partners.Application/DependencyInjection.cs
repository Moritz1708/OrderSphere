using OrderSphere.BuildingBlocks.Behaviors;

namespace OrderSphere.Partners.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPartnersApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddTransient(typeof(INotificationHandler<>), typeof(DomainEventLoggingHandler<>));
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
