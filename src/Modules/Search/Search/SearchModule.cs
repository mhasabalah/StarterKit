using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Search.Domains.Indexes.Jobs;
using Search.Domains.Indexes.Services;
using Search.Observability;
using Shared.Core.Mapster;

namespace Search;

/// <summary>
/// The Search module: one index store behind an engine port, fed by the modules' index definitions (ADR-0036). The
/// host discovers it through <see cref="SearchModuleManifest"/>.
/// </summary>
public static class SearchModule
{
    public const string ReconcileJobId = "search-reconcile-indexes";
    public const string DriftJobId = "search-detect-drift";

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
        services.TryAddSingleton<SearchMetrics>();

        AddEngine(services, configuration);

        // Indexing pipeline: the outbox consumer (discovered by the host) sends ApplySearchDocumentChangesCommand.
        services.AddScoped<SearchIndexer>();
        services.AddTransient<RebuildSearchIndexJob>();
        services.AddTransient<ReconcileSearchIndexesJob>();
        services.AddTransient<DetectSearchDriftJob>();

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

        using var scope = app.ApplicationServices.CreateScope();
        var services = scope.ServiceProvider;
        var options = services.GetRequiredService<IOptions<SearchOptions>>().Value;
        var recurring = services.GetRequiredService<IRecurringJobManager>();

        if (!services.GetRequiredService<IConfiguration>().GetValue("Hangfire:Servers:Search:Enabled", true))
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SearchModule))
                .LogWarning("This instance does not process the '{Queue}' queue (Hangfire:Servers:Search:Enabled=false); " +
                            "a worker replica must run it or indexes are never rebuilt", SearchOptions.Queue);
        }

        recurring.AddOrUpdate<ReconcileSearchIndexesJob>(ReconcileJobId, x => x.ExecuteAsync(JobCancellationToken.Null),
            options.Reconcile.Cron);
        recurring.AddOrUpdate<DetectSearchDriftJob>(DriftJobId, x => x.ExecuteAsync(JobCancellationToken.Null),
            options.Drift.Cron);

        // A new index (or a changed definition) starts building at once, not at the next reconcile tick.
        if (services.GetRequiredService<SearchIndexRegistry>().Indexes.Count > 0)
            recurring.Trigger(ReconcileJobId);

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
