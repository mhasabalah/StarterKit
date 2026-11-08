using Carter;

namespace Shared.Core.Extensions;

public static class CarterExtentions
{
    public static IServiceCollection AddCarterWithAssemblies
        (this IServiceCollection services, params Assembly[] assemblies)
    {
        var modules = ModulesIn(assemblies);

        // An assembly without endpoints (a module that has none yet) registers nothing. Calling AddCarter with no
        // modules would make Carter scan EVERY loaded assembly and register every other module's endpoints a second
        // time ("Duplicate endpoint name" at startup).
        if (modules.Length == 0)
        {
            return services;
        }

        services.AddCarter(configurator: config =>
        {
            // Validation runs in the mediator ValidationBehavior (validators registered per module by AddMediator).
            // Carter would otherwise scan EVERY loaded assembly for validators and register them as IValidator,
            // including open generic bases such as BaseQueryValidator<TQuery>, which breaks container build.
            config.WithEmptyValidators();
            config.WithModules(modules);
        });

        return services;
    }

    internal static Type[] ModulesIn(IEnumerable<Assembly> assemblies) =>
    [
        .. assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(ICarterModule)))
    ];
}
