using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Search.Contracts.Indexing;
using Search.Engine;
using Shared.Core.Modularity;

namespace Search.Tests;

public class ModuleRegistrationTests
{
    [Fact]
    public void The_manifest_is_discoverable_and_runs_after_the_platform_modules()
    {
        var catalog = ModuleCatalog.FromAssemblies([typeof(SearchModuleManifest).Assembly]);

        catalog.Modules.Should().ContainSingle().Which.Should().BeOfType<SearchModuleManifest>()
            .Which.Order.Should().Be(ModuleOrder.Default);
        catalog.Names.Should().Equal("Search");
    }

    [Fact]
    public void AddSearchIndex_registers_the_definition_and_its_source_by_key()
    {
        var services = new ServiceCollection();
        services.AddSearchIndex<ArticleSource>(TestIndexes.Articles());
        services.AddSingleton<SearchIndexRegistry>();
        using var provider = services.BuildServiceProvider();

        SearchModule.VerifyIndexes(provider);

        provider.GetRequiredService<SearchIndexRegistry>().Find("test.articles").Should().NotBeNull();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredKeyedService<ISearchDocumentSource>("test.articles").Should().BeOfType<ArticleSource>();
    }

    [Fact]
    public void A_definition_without_a_source_stops_the_host()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TestIndexes.Articles());
        services.AddSingleton<SearchIndexRegistry>();
        using var provider = services.BuildServiceProvider();

        FluentActions.Invoking(() => SearchModule.VerifyIndexes(provider))
            .Should().Throw<InvalidOperationException>().WithMessage("*without a document source: test.articles*");
    }

    [Fact]
    public void An_invalid_definition_stops_the_host_on_first_resolve()
    {
        var services = new ServiceCollection();
        services.AddSearchIndex<ArticleSource>(TestIndexes.Articles() with { TextFields = [] });
        services.AddSingleton<SearchIndexRegistry>();
        using var provider = services.BuildServiceProvider();

        FluentActions.Invoking(() => SearchModule.VerifyIndexes(provider))
            .Should().Throw<InvalidOperationException>().WithMessage("*at least one text field*");
    }

    [Theory]
    [InlineData("Postgres", typeof(global::Search.Engine.Postgres.PostgresSearchEngine), false)]
    [InlineData("Meilisearch", typeof(global::Search.Engine.Meilisearch.MeilisearchSearchEngine), true)]
    public void The_configured_engine_is_the_one_registered(string engine, Type implementation, bool healthCheck)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:starterkit"] = "Host=localhost;Database=unused",
            ["Search:Engine"] = engine,
            ["Search:Meilisearch:Url"] = "http://meilisearch:7700",
            ["Search:Meilisearch:ApiKey"] = "key"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSearchModule(configuration);

        services.Single(d => d.ServiceType == typeof(ISearchEngine)).ImplementationType.Should().Be(implementation);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations ?? [];
        if (healthCheck)
        {
            registrations.Should().ContainSingle(r => r.Name == "search-engine")
                .Which.FailureStatus.Should().Be(HealthStatus.Degraded, "owners fall back to SQL: an outage never takes the API out of rotation");
        }
        else
        {
            registrations.Should().NotContain(r => r.Name == "search-engine");
        }
    }

    private sealed class ArticleSource : ISearchDocumentSource
    {
        public Task<IReadOnlyList<SearchDocument>> GetAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SearchDocument>>([]);

        public async IAsyncEnumerable<SearchDocument> StreamAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
