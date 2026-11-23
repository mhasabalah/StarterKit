using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.Core.Mapster;

namespace Search;

/// <summary>
/// The Search module: one index store behind an engine port, fed by the modules' index definitions (ADR-0036). The
/// host discovers it through <see cref="SearchModuleManifest"/>.
/// </summary>
public static class SearchModule
{
    public static IServiceCollection AddSearchModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediator(SearchRoot.AssemblyName);
        services.AddCustomMapster(SearchRoot.AssemblyName);
        services.AddCarterWithAssemblies(SearchRoot.AssemblyName);

        services.AddCustomDbContext<SearchDbContext>(configuration, DatabaseDefaults.ConnectionStringName);

        services.AddOptions<SearchOptions>()
            .Bind(configuration.GetSection(SearchOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>());

        // The modules' definitions (AddSearchIndex), validated once: the first resolve throws on a bad definition.
        services.TryAddSingleton<SearchIndexRegistry>();
        services.TryAddSingleton(TimeProvider.System);

        AddEngine(services, configuration);
        return services;
    }

    /// <summary>One engine per deployment (<c>Search:Engine</c>); there is no silent fallback to another one.</summary>
    private static void AddEngine(IServiceCollection services, IConfiguration configuration)
    {
        var engine = configuration.GetSection(SearchOptions.SectionName)
            .GetValue<SearchEngineKind?>(nameof(SearchOptions.Engine)) ?? SearchEngineKind.Postgres;

        switch (engine)
        {
            case SearchEngineKind.Postgres:
                services.AddScoped<ISearchEngine, Engine.Postgres.PostgresSearchEngine>();
                break;
            case SearchEngineKind.Meilisearch:
                throw new InvalidOperationException(
                    "Search:Engine=Meilisearch is not available in this version yet; use Postgres.");
            default:
                throw new InvalidOperationException($"Search:Engine '{engine}' is not an engine.");
        }
    }

    public static IApplicationBuilder UseSearchModule(this IApplicationBuilder app)
    {
        app.UseMigration<SearchDbContext>(); // migrations (dev/tests; production migrates at deploy) + seeders

        VerifyIndexes(app.ApplicationServices);
        return app;
    }

    /// <summary>
    /// Fail fast: every definition is valid and has its document source. <c>AddSearchIndex</c> registers both, so a
    /// missing source means a definition was registered by hand.
    /// </summary>
    internal static void VerifyIndexes(IServiceProvider services)
    {
        var registry = services.GetRequiredService<SearchIndexRegistry>();
        var keyed = services.GetRequiredService<IServiceProviderIsKeyedService>();

        var missing = registry.Indexes
            .Where(index => !keyed.IsKeyedService(typeof(ISearchDocumentSource), index.Key))
            .Select(index => index.Key)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Search indexes without a document source: {string.Join(", ", missing)}. Register them with AddSearchIndex<TSource>(definition).");
        }
    }
}
