using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Mapster;
#if (OUTBOX)
using Shared.Core.Messaging.Outbox;
#endif

namespace ModuleName;

/// <summary>
/// The ModuleName module. The host discovers it through <see cref="ModuleNameModuleManifest"/> (ADR-0019): nothing in
/// Program.cs, Api.csproj or the architecture tests needs editing.
/// </summary>
public static class ModuleNameModule
{
    public static IServiceCollection AddModuleNameModule(this IServiceCollection services, IConfiguration configuration)
    {
#if (EXAMPLE)
        // One repository per aggregate root (command handlers); query handlers use the DbContext directly.
        services.AddScoped<IItemRepository, ItemRepository>();

#endif
        services.AddMediator(ModuleNameRoot.AssemblyName);
        services.AddCustomMapster(ModuleNameRoot.AssemblyName);
        services.AddCarterWithAssemblies(ModuleNameRoot.AssemblyName);

        services.AddCustomDbContext<ModuleNameDbContext>(configuration, DatabaseDefaults.ConnectionStringName);
#if (OUTBOX)
        services.AddOutbox<ModuleNameDbContext>(); // delivers the integration events raised by ModuleName aggregates
#endif

        // Hangfire jobs, seeders and options: see the add-background-job skill and Catalog/Webhooks for the patterns.
        return services;
    }

    public static IApplicationBuilder UseModuleNameModule(this IApplicationBuilder app)
    {
        app.UseMigration<ModuleNameDbContext>(); // migrations (dev/tests; production migrates at deploy) + seeders
        return app;
    }
}
