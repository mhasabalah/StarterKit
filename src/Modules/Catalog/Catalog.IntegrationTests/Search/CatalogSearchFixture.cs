using Catalog.Domains.Products.SearchIndex;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Search;
using Search.Data;
using Search.Domains.Indexes.Features.ApplySearchDocumentChanges;
using Search.Domains.Indexes.Jobs;
using Shared.Contracts.Mediation;
using Shared.Core.Messaging.Outbox;

namespace Catalog.IntegrationTests.Search;

/// <summary>
/// Catalog AND the real Search module on one PostgreSQL, the way the host wires them: Catalog registers the
/// <c>catalog.products</c> index, Search stores it in the PostgreSQL engine, and product queries go through the indexed
/// search (<c>Catalog:Search:UseSearchIndex=true</c>). There is no bus: <see cref="DeliverSearchEventsAsync"/> plays the
/// outbox relay and the Search consumer, from the search events Catalog wrote to its outbox.
/// </summary>
public sealed class CatalogSearchFixture : CatalogPostgresFixture
{
    private const string SearchEventType = "urn:message:search.documents-changed.v1";

    private static readonly System.Text.Json.JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record SearchEventPayload(string IndexKey, List<string> DocumentIds);

    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Catalog:Search:UseSearchIndex", "true"),
        new("Search:Analytics:Enabled", "false")
    ];

    protected override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new Mock<IBackgroundJobClient>().Object);
        services.AddSearchModule(configuration);
    }

    protected override async Task InitializeMoreAsync(IServiceProvider services) =>
        await services.GetRequiredService<SearchDbContext>().Database.MigrateAsync();

    /// <summary>
    /// Applies every search event still in Catalog's outbox (the relay + <c>SearchDocumentsChangedConsumer</c>), then
    /// marks them processed. Returns how many documents were applied.
    /// </summary>
    public async Task<int> DeliverSearchEventsAsync()
    {
        var events = await WithDb(async db => await db.Set<TenantOutboxMessage>()
            .Where(m => m.MessageType == SearchEventType && m.ProcessedAt == null)
            .ToListAsync());

        var ids = events
            .SelectMany(e => System.Text.Json.JsonSerializer.Deserialize<SearchEventPayload>(e.Payload, PayloadOptions)!.DocumentIds)
            .Distinct()
            .ToList();

        foreach (var chunk in ids.Chunk(100))
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new ApplySearchDocumentChangesCommand(ProductSearchIndex.Key, chunk));
        }

        await WithDb(async db =>
        {
            var processed = events.Select(e => e.Id).ToList();
            return await db.Set<TenantOutboxMessage>().Where(m => processed.Contains(m.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, DateTime.UtcNow));
        });
        return ids.Count;
    }

    /// <summary>A full (forced) rebuild of the products index: what the reconcile job does on first deploy.</summary>
    public async Task<RebuildResult> RebuildAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RebuildSearchIndexJob>()
            .ExecuteAsync(ProductSearchIndex.Key, true, null);
    }
}

[CollectionDefinition(Name)]
public sealed class CatalogSearchCollection : ICollectionFixture<CatalogSearchFixture>
{
    public const string Name = "catalog-search";
}
