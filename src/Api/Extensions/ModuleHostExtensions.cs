using Shared.Core.Extensions;
using Shared.Core.Modularity;
using Shared.Messaging.Extensions;

namespace Api.Extensions;

/// <summary>
/// Plugs the discovered modules into the host (ADR-0019). The module list comes from the build graph: every module
/// <c>ProjectReference</c> of <c>Api.csproj</c> becomes an <see cref="ModuleAssemblyAttribute"/> on this assembly.
/// </summary>
public static class ModuleHostExtensions
{
    /// <summary>
    /// Discovers the modules, registers MassTransit once with their consumer assemblies and the enum lookup with
    /// their enum assemblies, then calls each module's <see cref="IModule.AddServices"/> in startup order.
    /// </summary>
    public static ModuleCatalog AddModules(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var catalog = ModuleCatalog.FromHost(typeof(ModuleHostExtensions).Assembly);
        builder.Services.AddSingleton(catalog);

        // Transport: Messaging:Transport - InMemory | PostgreSql | RabbitMq.
        // Sagas: each module's ConfigureMessaging (durable, in the module's own DbContext - ADR-0023).
        builder.Services.AddMassTransitWithAssemblies(builder.Configuration,
            bus =>
            {
                foreach (var module in catalog.Modules)
                {
                    module.ConfigureMessaging(bus);
                }
            },
            catalog.ConsumerAssemblies);
        builder.Services.AddEnumLookup(catalog.EnumAssemblies);

        foreach (var module in catalog.Modules)
        {
            module.AddServices(builder);
        }

        return catalog;
    }

    /// <summary>Initializes every module (migrations + seeds, recurring jobs, hubs) in startup order.</summary>
    public static WebApplication UseModules(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        foreach (var module in app.Services.GetRequiredService<ModuleCatalog>().Modules)
        {
            module.Initialize(app);
        }

        return app;
    }
}
