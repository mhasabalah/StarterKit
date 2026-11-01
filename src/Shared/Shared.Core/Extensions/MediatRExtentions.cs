using Shared.Core.Behaviors;

namespace Shared.Core.Extensions;

public static class MediatRExtentions
{
    public static IServiceCollection AddMediatR(this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(assemblies);
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
            config.AddOpenBehavior(typeof(ConcurrencyHandlingBehavior<,>));
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            // Must be last — dispatches domain events after the handler and all
            // nested ISender.Send calls have fully committed their data.
            config.AddOpenBehavior(typeof(DispatchDomainEventsBehavior<,>));
        });

        services.AddValidatorsFromAssemblies(assemblies);
        return services;
    }
}
