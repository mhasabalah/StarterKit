using Carter;

namespace Shared.Core.Extensions;

public static class CarterExtentions
{
    public static IServiceCollection AddCarterWithAssemblies
        (this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddCarter(configurator: config =>
        {
            // Validation runs in the mediator ValidationBehavior (validators registered per module by AddMediator).
            // Carter would otherwise scan EVERY loaded assembly for validators and register them as IValidator,
            // including open generic bases such as BaseQueryValidator<TQuery>, which breaks container build.
            config.WithEmptyValidators();

            foreach (var assembly in assemblies)
            {
                var modules = assembly.GetTypes()
                    .Where(t => t.IsAssignableTo(typeof(ICarterModule))).ToArray();

                config.WithModules(modules);
            }
        });

        return services;
    }
}
