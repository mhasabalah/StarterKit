using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Search.Domains.Analytics.Jobs;
using Search.Domains.Analytics.Services;
using Search.Domains.Indexes.Jobs;
using Search.Domains.Indexes.Services;
using Search.Engine.Meilisearch;
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
    public const string PurgeAnalyticsJobId = "search-purge-analytics";

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

        // Analytics: counted in memory per replica, flushed per tenant, purged after the retention.
        services.TryAddSingleton<SearchAnalyticsBuffer>();
        services.TryAddSingleton<SearchAnalyticsRecorder>();
        services.AddScoped<SearchAnalyticsStore>();
        services.AddHostedService<SearchAnalyticsFlushService>();
        services.AddTransient<PurgeSearchAnalyticsJob>();

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
                AddMeilisearch(services, configuration);
                break;
            default:
                throw new InvalidOperationException($"Search:Engine '{engine}' is not an engine.");
        }
    }

    /// <summary>
    /// The Meilisearch adapter: a typed HTTP client with a per-request timeout (<c>Meilisearch:TimeoutSeconds</c>) and a
    /// circuit breaker, so an outage fails searches fast and owners fall back to SQL. No HTTP retries: indexing is
    /// retried by its consumer or job, which knows what was applied. The health check reports Degraded only.
    /// </summary>
    private static void AddMeilisearch(IServiceCollection services, IConfiguration configuration)
    {
        var timeout = TimeSpan.FromSeconds(configuration.GetSection(SearchOptions.SectionName)
            .GetValue($"{nameof(SearchOptions.Meilisearch)}:{nameof(SearchMeilisearchOptions.TimeoutSeconds)}",
                new SearchMeilisearchOptions().TimeoutSeconds));

        services.TryAddSingleton<MeilisearchIndexCache>();
        services.AddScoped<MeilisearchIndexes>();
        services.AddScoped<ISearchEngine, MeilisearchSearchEngine>();

        services.AddHttpClient<MeilisearchClient>((sp, http) =>
            {
                var meilisearch = sp.GetRequiredService<IOptions<SearchOptions>>().Value.Meilisearch;
                http.BaseAddress = new Uri(meilisearch.Url!.TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", meilisearch.ApiKey);
                http.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns the timeouts
            })
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                o.AttemptTimeout.Timeout = timeout;
                o.TotalRequestTimeout.Timeout = timeout;
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(30).Ticks, timeout.Ticks * 2));
            });

        services.AddHealthChecks().AddCheck<MeilisearchHealthCheck>(
            MeilisearchHealthCheck.Name,
            failureStatus: HealthStatus.Degraded,
            tags: [Shared.Core.HealthCheck.HealthCheckTags.Ready]);
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
        recurring.AddOrUpdate<PurgeSearchAnalyticsJob>(PurgeAnalyticsJobId, x => x.ExecuteAsync(JobCancellationToken.Null),
            options.Analytics.PurgeCron);

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
