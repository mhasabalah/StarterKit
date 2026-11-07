using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Shared.Core.MultiTenancy;

namespace Catalog.Domains.Categories.Cache;

/// <summary>
/// Redis (IDistributedCache) cache of the active-category snapshot behind the public category tree.
/// It stores the culture-neutral snapshot, so one entry serves
/// both languages, PER TENANT (key built by ITenantCacheKeys). Explicit invalidation on every category change (CategoryChangedEvent) is the primary
/// mechanism; the absolute TTL is a safety net. Cache failures are swallowed with a warning — the cache must
/// never take the endpoint down; callers fall back to the database.
/// </summary>
public interface ICategoryTreeCache
{
    Task<IReadOnlyList<CategoryNodeSnapshot>?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(IReadOnlyList<CategoryNodeSnapshot> nodes, CancellationToken cancellationToken = default);

    Task InvalidateAsync(CancellationToken cancellationToken = default);
}

public sealed class CategoryTreeCache(
    IDistributedCache cache,
    ITenantCacheKeys tenantKeys,
    ILogger<CategoryTreeCache> logger) : ICategoryTreeCache
{
    /// <summary>Tenant-relative key; the stored key is <c>t:{tenantId}:catalog:categories:tree:v1</c>.</summary>
    public const string CacheKey = "catalog:categories:tree:v1";

    private string Key => tenantKeys.For(CacheKey);

    // A bulk import raises one CategoryChangedEvent per row; this scoped instance removes the key once until the tree is
    // written again (Set), so a batch costs one round trip instead of one per category.
    private bool _invalidated;

    private static readonly DistributedCacheEntryOptions EntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
    };

    public async Task<IReadOnlyList<CategoryNodeSnapshot>?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await cache.GetStringAsync(Key, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<List<CategoryNodeSnapshot>>(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading the category tree from cache failed; falling back to the database");
            return null;
        }
    }

    public async Task SetAsync(IReadOnlyList<CategoryNodeSnapshot> nodes, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetStringAsync(Key, JsonSerializer.Serialize(nodes), EntryOptions, cancellationToken);
            _invalidated = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not cache the category tree");
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        if (_invalidated)
            return;

        try
        {
            await cache.RemoveAsync(Key, cancellationToken);
            _invalidated = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not invalidate the category tree cache (it expires on its own)");
        }
    }
}
