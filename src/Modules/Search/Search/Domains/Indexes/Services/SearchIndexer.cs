using Microsoft.Extensions.DependencyInjection;
using Search.Observability;

namespace Search.Domains.Indexes.Services;

/// <summary>The outcome of bringing documents of an index in line with their source.</summary>
public sealed record IndexChangeResult(int Upserted, int Deleted);

/// <summary>
/// Brings documents of an index in line with their source, in the current tenant: it reads the CURRENT state of the
/// ids from the owner's <see cref="ISearchDocumentSource"/> (so delivery order and repeats do not matter), writes the
/// documents that exist and removes the ones that do not. Live writes carry the index's write stamp, so a rebuild in
/// progress keeps them.
/// </summary>
public sealed class SearchIndexer(
    IServiceProvider services,
    SearchDbContext db,
    ISearchEngine engine,
    ITenantContext tenantContext,
    SearchMetrics metrics)
{
    public async Task<IndexChangeResult> ApplyChangesAsync(
        RegisteredIndex index, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(ids);
        var wanted = ids.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
            return new IndexChangeResult(0, 0);

        var tenantId = tenantContext.TenantId
                       ?? throw new InvalidOperationException("Search documents are indexed in a tenant scope.");
        var source = services.GetRequiredKeyedService<ISearchDocumentSource>(index.Key);

        var wantedSet = wanted.ToHashSet(StringComparer.Ordinal);
        var present = (await source.GetAsync(wanted, cancellationToken))
            .Where(d => wantedSet.Contains(d.Id))
            .Select(d => SearchDocumentPreparer.Prepare(index, d))
            .ToList();
        var presentIds = present.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var absent = wanted.Where(id => !presentIds.Contains(id)).ToList();

        var scope = new IndexScope(index, tenantId);
        var stamp = await WriteStampAsync(index.Key, cancellationToken);
        await engine.UpsertAsync(scope, present, stamp, cancellationToken);
        await engine.DeleteAsync(scope, absent, cancellationToken);

        metrics.Indexed(index.Key, present.Count, absent.Count);
        return new IndexChangeResult(present.Count, absent.Count);
    }

    /// <summary>The stamp of the rebuild in progress, or of the last completed one (0 before any).</summary>
    private async Task<long> WriteStampAsync(string indexKey, CancellationToken cancellationToken) =>
        await db.IndexStates.AsNoTracking()
            .Where(s => s.IndexKey == indexKey)
            .Select(s => (long?)(s.BuildingStamp ?? s.ActiveStamp))
            .FirstOrDefaultAsync(cancellationToken) ?? 0;
}
