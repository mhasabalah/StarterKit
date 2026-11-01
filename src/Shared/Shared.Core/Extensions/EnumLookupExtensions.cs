using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Core.Enums;

namespace Shared.Core.Extensions;

/// <summary>
/// Extension methods for registering the Hybrid EnumLookupService.
/// </summary>
public static class EnumLookupExtensions
{
    /// <summary>
    /// Registers the basic EnumLookupService (reflection-based only).
    /// Use this for simple projects without custom enum handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies containing enum types to register.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEnumLookup(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddSingleton<IEnumLookupService>(sp =>
        {
            var cache = sp.GetRequiredService<IMemoryCache>();
            var logger = sp.GetRequiredService<ILogger<EnumLookupService>>();
            return new EnumLookupService(cache, logger, assemblies);
        });

        return services;
    }

    /// <summary>
    /// Registers the Hybrid EnumLookupService (reflection + custom handlers).
    /// Use this for projects that need custom logic for specific enums.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies containing enum types to register.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHybridEnumLookup(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        // Register custom IEnumHandler implementations before calling this, e.g.
        // services.AddScoped<IEnumHandler, MyCustomEnumHandler>();

        services.AddSingleton<IEnumLookupService>(sp =>
        {
            var cache = sp.GetRequiredService<IMemoryCache>();
            var logger = sp.GetRequiredService<ILogger<HybridEnumLookupService>>();
            var handlers = sp.GetServices<IEnumHandler>();

            return new HybridEnumLookupService(cache, logger, handlers, assemblies);
        });

        return services;
    }
}
