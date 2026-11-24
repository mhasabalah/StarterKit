using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using static Search.Engine.Meilisearch.MeilisearchNames;

namespace Search.Engine.Meilisearch;

/// <summary>
/// <see cref="ISearchEngine"/> on a Meilisearch server (ADR-0036). One Meilisearch index per index key, shared by every
/// tenant: every request carries the tenant filter (<see cref="MeilisearchFilterCompiler"/>), every document its tenant
/// in its key. This class only orchestrates; naming, settings, documents, filters and queries are separate pure parts.
/// <list type="bullet">
/// <item>No stemming and no version guard (<see cref="Capabilities"/>): within a batch the newest version wins, and the
/// drift check repairs an older write that landed after a newer one.</item>
/// <item>Close spellings use Meilisearch's typo tolerance (one typo from 5 letters, two from 9), not a similarity
/// threshold; <see cref="EngineQuery.FuzzyThreshold"/> does not apply.</item>
/// <item>A query with OR alternatives is a federated search: its total is an estimate and its facet counts are not
/// exhaustive.</item>
/// </list>
/// </summary>
internal sealed class MeilisearchSearchEngine(
    MeilisearchClient client,
    MeilisearchIndexes indexes,
    SearchIndexRegistry registry,
    TimeProvider time) : ISearchEngine
{
    private const int StampPageSize = 1000;

    public SearchEngineCapabilities Capabilities { get; } = new(MeilisearchClient.EngineName, Stemming: false, VersionGuard: false);

    public Task EnsureIndexAsync(RegisteredIndex index, CancellationToken cancellationToken) =>
        indexes.EnsureAsync(index, force: true, cancellationToken);

    public async Task UpsertAsync(IndexScope scope, IReadOnlyList<EngineDocument> documents, long stamp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (documents.Count == 0)
            return;

        var uid = await indexes.EnsureAsync(scope.Index, force: false, cancellationToken);
        var indexedAt = time.GetUtcNow().UtcDateTime;
        var batch = new JsonArray([.. MeilisearchDocuments.LatestPerId(documents)
            .Select(d => (JsonNode)MeilisearchDocuments.ToJson(scope.TenantId, d, stamp, indexedAt))]);

        await client.RunTaskAsync(HttpMethod.Post, $"indexes/{uid}/documents?primaryKey={Key}", batch, cancellationToken);
    }

    public async Task DeleteAsync(IndexScope scope, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (ids.Count == 0)
            return;

        var keys = new JsonArray([.. ids.Select(id => (JsonNode)DocumentKey(scope.TenantId, id))]);
        await IgnoringMissingIndexAsync(() =>
            client.RunTaskAsync(HttpMethod.Post, $"indexes/{indexes.Uid(scope.Index)}/documents/delete-batch", keys, cancellationToken));
    }

    public async Task<int> SweepAsync(IndexScope scope, long stamp, DateTime startedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var task = await IgnoringMissingIndexAsync(() => client.RunTaskAsync(HttpMethod.Post,
            $"indexes/{indexes.Uid(scope.Index)}/documents/delete",
            new JsonObject { ["filter"] = MeilisearchFilterCompiler.Sweep(scope, stamp, startedAt) }, cancellationToken));
        return task?["details"]?["deletedDocuments"]?.GetValue<int>() ?? 0;
    }

    public async Task<EngineSearchResult> SearchAsync(IndexScope scope, EngineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var uid = await indexes.EnsureAsync(scope.Index, force: false, cancellationToken);
        var filter = MeilisearchFilterCompiler.Compile(scope, query.Filters);

        var mode = query.Text.IsEmpty ? SearchMatchMode.Browse : SearchMatchMode.Exact;
        var exact = await RunAsync(uid, MeilisearchQueryText.Build(query.Text), filter, scope, query, mode, cancellationToken);
        if (exact.Total > 0 || !MeilisearchQueryText.AllowsFuzzy(query.Text, query.FuzzyMinTermLength))
            return exact;

        var fuzzy = await RunAsync(uid, MeilisearchQueryText.Build(query.Text, query.FuzzyMinTermLength), filter, scope, query,
            SearchMatchMode.Fuzzy, cancellationToken);
        return fuzzy.Hits.Count > 0 ? fuzzy : exact;
    }

    public async Task<int> CountAsync(IndexScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var page = await FetchAsync(scope, offset: 0, limit: 0, cancellationToken);
        return page?["total"]?.GetValue<int>() ?? 0;
    }

    public async IAsyncEnumerable<SearchDocumentStamp> StreamStampsAsync(
        IndexScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        for (var offset = 0; ; offset += StampPageSize)
        {
            var page = await FetchAsync(scope, offset, StampPageSize, cancellationToken);
            var results = page?["results"]?.AsArray() ?? [];
            foreach (var document in results)
                yield return new SearchDocumentStamp(document![Id]!.GetValue<string>(), document[DocumentVersion]!.GetValue<long>());

            if (results.Count < StampPageSize)
                yield break;
        }
    }

    public async Task DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        foreach (var index in registry.Indexes)
        {
            await IgnoringMissingIndexAsync(() => client.RunTaskAsync(HttpMethod.Post,
                $"indexes/{indexes.Uid(index)}/documents/delete",
                new JsonObject { ["filter"] = MeilisearchFilterCompiler.ForTenant(tenantId) }, cancellationToken));
        }
    }

    private async Task<EngineSearchResult> RunAsync(
        string uid, IReadOnlyList<string> qs, string filter, IndexScope scope, EngineQuery query, SearchMatchMode mode,
        CancellationToken cancellationToken)
    {
        if (qs.Count == 1)
        {
            var single = await client.SendAsync(HttpMethod.Post, $"indexes/{uid}/search",
                MeilisearchSearches.Single(qs[0], filter, query, scope.Index), cancellationToken);
            return MeilisearchSearches.Read(single!, query, mode, exhaustiveFacets: true);
        }

        var federated = await client.SendAsync(HttpMethod.Post, "multi-search",
            MeilisearchSearches.Federated(uid, qs, filter, query, scope.Index), cancellationToken);
        return MeilisearchSearches.Read(federated!, query, mode, exhaustiveFacets: false);
    }

    /// <summary>Every document of the tenant (all branches), reading only the id and version.</summary>
    private Task<JsonNode?> FetchAsync(IndexScope scope, int offset, int limit, CancellationToken cancellationToken) =>
        IgnoringMissingIndexAsync(() => client.SendAsync(HttpMethod.Post, $"indexes/{indexes.Uid(scope.Index)}/documents/fetch",
            new JsonObject
            {
                ["filter"] = MeilisearchFilterCompiler.ForTenant(scope.TenantId),
                ["fields"] = new JsonArray((JsonNode)Id, (JsonNode)DocumentVersion),
                ["offset"] = offset,
                ["limit"] = limit
            }, cancellationToken));

    /// <summary>An index that was never created holds no documents: nothing to delete, count or stream.</summary>
    private static async Task<T?> IgnoringMissingIndexAsync<T>(Func<Task<T>> call)
        where T : class?
    {
        try
        {
            return await call();
        }
        catch (MeilisearchApiException ex) when (ex.Code == MeilisearchApiException.IndexNotFound)
        {
            return null;
        }
    }
}
