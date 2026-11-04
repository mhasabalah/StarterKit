using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Caching;

namespace Tenancy.Services;

/// <summary>
/// <see cref="ITenantStore"/> backed by <see cref="TenancyDbContext"/> with a two-level cache: in-process
/// (<c>MultiTenancy:Cache:LocalSeconds</c>) over distributed/Redis (<c>DistributedSeconds</c>). Misses are cached
/// briefly too (unknown hosts are probed on every request otherwise). <see cref="TenantChangedEvent"/> invalidates
/// both levels and is broadcast (<see cref="ILocalCacheInvalidationBus"/>, Redis pub/sub) so other instances evict their
/// L1 entry immediately; without Redis or when the broadcast fails they expire within LocalSeconds.
/// Cache failures never fail a request: the database is the fallback.
/// </summary>
public sealed class TenantStore(
    IUnitOfWorkRunner runner,
    IMemoryCache memoryCache,
    IDistributedCache distributedCache,
    IOptions<MultiTenancyOptions> options,
    ILocalCacheInvalidationBus invalidationBus,
    ILogger<TenantStore> logger) : ITenantStore
{
    private const string Prefix = "tenancy:tenant:";
    private static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        GetAsync($"id:{tenantId:N}", db => db.Tenants.Where(t => t.Id == tenantId), cancellationToken);

    public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var normalized = Tenant.NormalizeIdentifier(identifier);
        return Tenant.IsValidIdentifier(normalized)
            ? GetAsync($"ident:{normalized}", db => db.Tenants.Where(t => t.Identifier == normalized), cancellationToken)
            : Task.FromResult<TenantInfo?>(null);
    }

    public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default)
    {
        var normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        return normalized.Length is 0 or > TenantDomain.HostMaxLength
            ? Task.FromResult<TenantInfo?>(null)
            : GetAsync($"host:{normalized}",
                db => db.Tenants.Where(t => t.Domains.Any(d => d.Host == normalized && d.VerifiedAt != null)),
                cancellationToken);
    }

    public Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default) =>
        runner.RunAsync<IReadOnlyList<TenantInfo>>(async (services, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var tenants = await services.GetRequiredService<TenancyDbContext>().Tenants.AsNoTracking()
                    .Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Suspended)
                    .OrderBy(t => t.CreatedAt)
                    .ToListAsync(ct);
                return tenants.Select(t => t.ToInfo()).ToList();
            }
        }, cancellationToken);

    /// <summary>Drops every cached lookup of a tenant (by id, identifier and each custom host).</summary>
    public async Task InvalidateAsync(Guid tenantId, string? identifier, IEnumerable<string> hosts,
        CancellationToken cancellationToken = default)
    {
        var keys = new List<string> { $"id:{tenantId:N}" };
        if (!string.IsNullOrEmpty(identifier)) keys.Add($"ident:{identifier}");
        keys.AddRange(hosts.Select(h => $"host:{h}"));

        foreach (var key in keys)
        {
            memoryCache.Remove(Prefix + key);
            try
            {
                await distributedCache.RemoveAsync(Prefix + key, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not invalidate cached tenant {Key}", key);
            }
        }

        // Broadcast LAST: peers that evict L1 on receipt re-read L2, which must already be gone, or they would
        // re-cache the stale value.
        await invalidationBus.PublishAsync(keys.Select(k => Prefix + k).ToList(), cancellationToken);
    }

    /// <summary>Tenants whose data is being moved: background fan-out skips them (see <see cref="GetServiceableTenantsAsync"/>).</summary>
    public Task<IReadOnlyList<TenantInfo>> GetMigratingTenantsAsync(CancellationToken cancellationToken = default) =>
        runner.RunAsync<IReadOnlyList<TenantInfo>>(async (services, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var tenants = await services.GetRequiredService<TenancyDbContext>().Tenants.AsNoTracking()
                    .Where(t => t.Status == TenantStatus.Migrating)
                    .ToListAsync(ct);
                return tenants.Select(t => t.ToInfo()).ToList();
            }
        }, cancellationToken);

    private async Task<TenantInfo?> GetAsync(string key, Func<TenancyDbContext, IQueryable<Tenant>> query,
        CancellationToken cancellationToken)
    {
        var cacheKey = Prefix + key;
        if (memoryCache.TryGetValue(cacheKey, out CachedTenant? local) && local is not null)
        {
            return local.Tenant;
        }

        var cached = await ReadDistributedAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            SetLocal(cacheKey, cached);
            return cached.Tenant;
        }

        var tenant = await runner.RunAsync(async (services, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var row = await query(services.GetRequiredService<TenancyDbContext>())
                    .AsNoTracking()
                    .FirstOrDefaultAsync(ct);
                return row?.ToInfo();
            }
        }, cancellationToken);

        var entry = new CachedTenant(tenant);
        SetLocal(cacheKey, entry);
        await WriteDistributedAsync(cacheKey, entry, cancellationToken);
        return tenant;
    }

    private void SetLocal(string key, CachedTenant entry)
    {
        var seconds = options.Value.Cache.LocalSeconds;
        if (seconds <= 0) return;
        var ttl = entry.Tenant is null ? TimeSpan.FromSeconds(Math.Min(seconds, MissTtl.TotalSeconds)) : TimeSpan.FromSeconds(seconds);
        memoryCache.Set(key, entry, ttl);
    }

    private async Task<CachedTenant?> ReadDistributedAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var json = await distributedCache.GetStringAsync(key, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<CachedTenant>(json, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading tenant cache {Key} failed; using the database", key);
            return null;
        }
    }

    private async Task WriteDistributedAsync(string key, CachedTenant entry, CancellationToken cancellationToken)
    {
        var seconds = options.Value.Cache.DistributedSeconds;
        if (seconds <= 0) return;
        try
        {
            var ttl = entry.Tenant is null ? MissTtl : TimeSpan.FromSeconds(seconds);
            await distributedCache.SetStringAsync(key, JsonSerializer.Serialize(entry, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Writing tenant cache {Key} failed", key);
        }
    }

    /// <summary>Wrapper so a cached "not found" is distinguishable from a cache miss.</summary>
    private sealed record CachedTenant(TenantInfo? Tenant);
}
