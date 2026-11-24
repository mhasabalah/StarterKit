using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Search.Contracts.Indexing;
using Search.Engine;
using Search.IntegrationTests.TestIndex;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;

namespace Search.IntegrationTests.Conformance;

/// <summary>
/// A real Meilisearch server (the image of <c>docker-compose.meilisearch.yml</c>) behind the REAL module, configured with
/// <c>Search:Engine=Meilisearch</c>: the adapter is resolved, configured and called exactly as in production. The test
/// index is created through <see cref="ISearchEngine.EnsureIndexAsync"/>, like the reconcile job does.
/// </summary>
public sealed class MeilisearchFixture : IAsyncLifetime, ISearchEngineTestHost
{
    public const string Image = "getmeili/meilisearch:v1.15.2";
    public const string MasterKey = "conformance-master-key-0123456789";
    public const string IndexPrefix = "conformance_";
    private const int Port = 7700;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithEnvironment("MEILI_MASTER_KEY", MasterKey)
        .WithEnvironment("MEILI_NO_ANALYTICS", "true")
        .WithEnvironment("MEILI_ENV", "production")
        .WithPortBinding(Port, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPath("/health").ForPort(Port)))
        .Build();

    private ServiceProvider? _provider;

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");

    public string Url => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

    public RegisteredIndex Index => Services.GetRequiredService<SearchIndexRegistry>().Find(TestArticles.Key)!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _provider = BuildProvider(MasterKey);

        await RunAsync(Guid.NewGuid(), async engine =>
        {
            await engine.EnsureIndexAsync(Index, CancellationToken.None);
            return true;
        });
    }

    /// <summary>The real module configured for Meilisearch, with <paramref name="apiKey"/> (and another server, if given).</summary>
    public ServiceProvider BuildProvider(string apiKey, string? url = null, IEnumerable<SearchIndexDefinition>? indexes = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never opened: the Meilisearch engine does not use the database.
                [$"ConnectionStrings:{DatabaseDefaults.ConnectionStringName}"] = "Host=localhost;Database=unused;Username=x;Password=x",
                ["Search:Engine"] = "Meilisearch",
                ["Search:Meilisearch:Url"] = url ?? Url,
                ["Search:Meilisearch:ApiKey"] = apiKey,
                ["Search:Meilisearch:IndexPrefix"] = IndexPrefix,
                ["Search:Meilisearch:TimeoutSeconds"] = "5"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddMultiTenancy(configuration);
        services.AddSingleton(new Mock<IHttpContextService> { DefaultValue = DefaultValue.Empty }.Object);
        services.AddSingleton(new ArticleStore());
        services.AddSearchModule(configuration);
        foreach (var definition in indexes ?? [TestArticles.Definition])
            services.AddSearchIndex<InMemoryArticleSource>(definition);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public Task<T> RunAsync<T>(Guid tenantId, Func<ISearchEngine, Task<T>> action, IReadOnlyList<Guid>? branches = null) =>
        RunAsync(Services, tenantId, action, branches);

    public static async Task<T> RunAsync<T>(
        IServiceProvider services, Guid tenantId, Func<ISearchEngine, Task<T>> action, IReadOnlyList<Guid>? branches = null)
    {
        using (TenantScope.Begin(SearchPostgresFixture.Tenant(tenantId), branches is null ? null : new BranchScope(branches)))
        {
            await using var scope = services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<ISearchEngine>());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class MeilisearchCollection : ICollectionFixture<MeilisearchFixture>
{
    public const string Name = "search-meilisearch";
}

/// <summary>The Meilisearch adapter, through the real module, against a real Meilisearch server.</summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchSearchEngineConformanceTests(MeilisearchFixture fixture) : SearchEngineConformanceTests(fixture);
