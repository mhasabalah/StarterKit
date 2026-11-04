using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Behaviors;
using Shared.Core.Data;
using Shared.Core.Mediation;

namespace Shared.Core.Extensions;

public static class MediatorExtensions
{
    /// <summary>
    /// A module's request/notification handlers + the standard pipeline (validation → concurrency → logging →
    /// domain-event dispatch) + its validators. Idempotent: every module calls it with its own assembly.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddMediatorHandlers(assemblies);

        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(ConcurrencyHandlingBehavior<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)));
        // Must be last — dispatches domain events after the handler and all
        // nested ISender.Send calls have fully committed their data.
        services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(DispatchDomainEventsBehavior<,>)));

        // DispatchDomainEventsBehavior needs the collector + dispatcher (TryAdd: idempotent).
        services.AddDomainEventDispatch();

        // Open generic validators are reusable bases (see BaseQueryValidator<TQuery>), never services.
        services.AddValidatorsFromAssemblies(assemblies, filter: r => !r.ValidatorType.IsGenericTypeDefinition);
        return services;
    }

    /// <summary>
    /// Only the <see cref="ISender"/>/<see cref="IPublisher"/> adapter and the handlers found in
    /// <paramref name="assemblies"/> (no behaviors, no validators). Request handlers are TryAdd (a stub registered
    /// first wins; one registered later overrides); notification handlers are added once per implementation.
    /// </summary>
    public static IServiceCollection AddMediatorHandlers(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.TryAddTransient<Mediator>();
        services.TryAddTransient<ISender>(sp => sp.GetRequiredService<Mediator>());
        services.TryAddTransient<IPublisher>(sp => sp.GetRequiredService<Mediator>());

        foreach (var type in assemblies.Distinct().SelectMany(SafeTypes))
        {
            if (type is not { IsClass: true, IsAbstract: false } || type.IsGenericTypeDefinition)
                continue;

            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IRequestHandler<,>))
                    services.TryAddTransient(contract, type);
                else if (definition == typeof(INotificationHandler<>))
                    services.TryAddEnumerable(ServiceDescriptor.Transient(contract, type));
            }
        }

        return services;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
