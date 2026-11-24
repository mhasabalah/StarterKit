using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Search.Engine;
using Search.Engine.Meilisearch;
using Search.Engine.Text;
using static Search.IntegrationTests.TestIndex.TestArticles;

namespace Search.IntegrationTests.Conformance;

/// <summary>
/// What is specific to the Meilisearch adapter, on a real server: the settings it applies, the least-privilege key it
/// needs, how an outage looks, and the documented differences (no stemming, estimated OR totals).
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchEngineTests(MeilisearchFixture fixture)
{
    /// <summary>The key of wiki/Search/Meilisearch.md: everything the API needs, nothing to manage keys.</summary>
    public static readonly string[] ApiKeyActions = ["search", "documents.*", "indexes.*", "settings.*", "tasks.get"];

    private HttpClient Admin()
    {
        var http = new HttpClient { BaseAddress = new Uri(fixture.Url + "/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MeilisearchFixture.MasterKey);
        return http;
    }

    private static IndexScope Scope(IServiceProvider services, Guid tenant) =>
        new(services.GetRequiredService<SearchIndexRegistry>().Find(Key)!, tenant);

    [Fact]
    public async Task The_index_gets_the_settings_of_its_definition()
    {
        using var admin = Admin();
        var uid = MeilisearchNames.IndexUid(MeilisearchFixture.IndexPrefix, Key);

        var settings = (await admin.GetFromJsonAsync<JsonObject>($"indexes/{uid}/settings"))!;
        var index = await admin.GetFromJsonAsync<JsonObject>($"indexes/{uid}");

        index!["primaryKey"]!.GetValue<string>().Should().Be(MeilisearchNames.Key);
        settings["searchableAttributes"]!.AsArray().Select(a => a!.GetValue<string>())
            .Should().Equal(MeilisearchIndexSettings.Build(fixture.Index, new Options.SearchOptions())["searchableAttributes"]!
                .AsArray().Select(a => a!.GetValue<string>()));
        settings["rankingRules"]!.AsArray().Select(a => a!.GetValue<string>()).Should().Equal(MeilisearchIndexSettings.RankingRules);
        settings["filterableAttributes"]!.AsArray().Select(a => a!.GetValue<string>()).Should().Contain(["_tenant", "_branch", "f_status"]);
    }

    [Fact]
    public async Task A_scoped_key_with_the_documented_actions_can_do_everything_the_engine_needs()
    {
        using var admin = Admin();
        var created = await admin.PostAsJsonAsync("keys", new
        {
            name = $"starterkit-api-{Guid.NewGuid():N}",
            actions = ApiKeyActions,
            indexes = new[] { $"{MeilisearchFixture.IndexPrefix}*" },
            expiresAt = (string?)null
        });
        var key = (await created.Content.ReadFromJsonAsync<JsonObject>())!["key"]!.GetValue<string>();
        await using var scoped = fixture.BuildProvider(key);
        var tenant = Guid.NewGuid();
        var scope = Scope(scoped, tenant);

        await MeilisearchFixture.RunAsync(scoped, tenant, async engine =>
        {
            await engine.EnsureIndexAsync(scope.Index, CancellationToken.None);
            await engine.UpsertAsync(scope, [SearchDocumentPreparer.Prepare(scope.Index, Article("a1", "Laptop"))], 1, CancellationToken.None);
            var found = await engine.SearchAsync(scope, new EngineQuery { Text = SearchQueryParser.Parse("laptop") }, CancellationToken.None);
            found.Hits.Select(h => h.Id).Should().Equal("a1");
            (await engine.CountAsync(scope, CancellationToken.None)).Should().Be(1);
            (await engine.SweepAsync(scope, 2, DateTime.UtcNow.AddMinutes(1), CancellationToken.None)).Should().Be(1);
            await engine.DeleteTenantAsync(tenant, CancellationToken.None);
            return true;
        });

        (await scoped.GetRequiredService<HealthCheckService>().CheckHealthAsync()).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task An_unreachable_server_is_an_unavailable_engine_and_a_degraded_health_check()
    {
        await using var down = fixture.BuildProvider(MeilisearchFixture.MasterKey, url: "http://127.0.0.1:1");
        var tenant = Guid.NewGuid();

        await FluentActions.Awaiting(() => MeilisearchFixture.RunAsync(down, tenant, engine =>
                engine.SearchAsync(Scope(down, tenant), new EngineQuery { Text = SearchQueryParser.Parse("laptop") }, CancellationToken.None)))
            .Should().ThrowAsync<SearchEngineUnavailableException>();

        var health = await down.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        health.Entries[MeilisearchHealthCheck.Name].Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task An_index_that_was_never_created_counts_streams_and_deletes_nothing()
    {
        var never = Definition with { Key = $"test.never{Guid.NewGuid():N}"[..20] };
        await using var services = fixture.BuildProvider(MeilisearchFixture.MasterKey, indexes: [never]);
        var tenant = Guid.NewGuid();
        var scope = new IndexScope(services.GetRequiredService<SearchIndexRegistry>().Find(never.Key)!, tenant);

        await MeilisearchFixture.RunAsync(services, tenant, async engine =>
        {
            (await engine.CountAsync(scope, CancellationToken.None)).Should().Be(0);
            (await engine.SweepAsync(scope, 5, DateTime.UtcNow, CancellationToken.None)).Should().Be(0);
            await engine.DeleteAsync(scope, ["x"], CancellationToken.None);
            await engine.DeleteTenantAsync(tenant, CancellationToken.None);
            var stamps = new List<SearchDocumentStamp>();
            await foreach (var stamp in engine.StreamStampsAsync(scope, CancellationToken.None))
                stamps.Add(stamp);
            stamps.Should().BeEmpty();
            return true;
        });
    }

    [Fact]
    public async Task Alternatives_answer_with_facets_that_say_they_are_not_exhaustive()
    {
        var tenant = Guid.NewGuid();
        var scope = Scope(fixture.Services, tenant);
        await fixture.RunAsync(tenant, async engine =>
        {
            await engine.UpsertAsync(scope,
                [.. new[] { Article("a", "Red lamp", tags: ["x"]), Article("b", "Blue lamp", tags: ["x"]) }
                    .Select(d => SearchDocumentPreparer.Prepare(scope.Index, d))], 1, CancellationToken.None);
            return true;
        });

        var result = await fixture.RunAsync(tenant, engine => engine.SearchAsync(scope, new EngineQuery
        {
            Text = SearchQueryParser.Parse("red or blue"),
            Facets = ["tag_ids"]
        }, CancellationToken.None));

        result.Hits.Select(h => h.Id).Should().BeEquivalentTo(["a", "b"]);
        result.FacetsExhaustive.Should().BeFalse("each alternative counts its own matches");
        result.Facets.Single().Values.Should().ContainSingle(v => v.Value == "x");
    }
}
