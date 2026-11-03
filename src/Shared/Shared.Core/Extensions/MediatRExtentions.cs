using Shared.Core.Behaviors;
using Shared.Core.Data;

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

        // DispatchDomainEventsBehavior needs the collector + dispatcher (TryAdd: idempotent).
        services.AddDomainEventDispatch();

        // Open generic validators are reusable bases (see BaseQueryValidator<TQuery>), never services.
        services.AddValidatorsFromAssemblies(assemblies, filter: r => !r.ValidatorType.IsGenericTypeDefinition);
        return services;
    }
}
