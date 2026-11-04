using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Caching;

namespace Tenancy.Services;

/// <summary>
/// <see cref="ITenantMembershipResolver"/>: the caller's ACTIVE membership in a tenant with its EFFECTIVE permissions
/// (union of tenant roles; system roles from code) and EXPANDED branch scope (assignments + descendants via the
/// materialized path). One cached lookup per request (L1 memory + L2 distributed).
/// <para>
/// Invalidation: membership changes drop the user's entry; role changes bump a per-tenant generation that is part
/// of every key of that tenant (so all memberships of the tenant miss at once without key scans).
/// </para>
/// <para>Branch scope rules: owner or <c>tenant.branches.all</c> → unrestricted; otherwise exactly the assigned
/// branches (+ subtrees). A member without assignments sees only tenant-wide rows (BranchId = null): secure default.</para>
/// </summary>
public sealed class TenantMembershipResolver(
    IUnitOfWorkRunner runner,
    IMemoryCache memoryCache,
    IDistributedCache distributedCache,
    IOptions<MultiTenancyOptions> options,
    ILocalCacheInvalidationBus invalidationBus,
    ILogger<TenantMembershipResolver> logger) : ITenantMembershipResolver
{
    private const string Prefix = "tenancy:m:";
    private const string GenerationPrefix = "tenancy:mgen:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TenantMembershipInfo?> GetMembershipAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var generation = await GetGenerationAsync(tenantId, cancellationToken);
        var key = $"{Prefix}{tenantId:N}:{userId:N}:{generation}";

        if (memoryCache.TryGetValue(key, out CachedMembership? local) && local is not null)
        {
            return local.ToInfo();
        }

        var cached = await TryReadAsync<CachedMembership>(key, cancellationToken);
        if (cached is null)
        {
            cached = await LoadAsync(userId, tenantId, cancellationToken);
            await TryWriteAsync(key, cached, cancellationToken);
        }

        if (options.Value.Cache.LocalSeconds > 0)
        {
            memoryCache.Set(key, cached, TimeSpan.FromSeconds(options.Value.Cache.LocalSeconds));
        }

        return cached.ToInfo();
    }

    public Task<IReadOnlyList<Guid>> GetTenantIdsOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        runner.RunAsync<IReadOnlyList<Guid>>(async (services, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var db = services.GetRequiredService<TenancyDbContext>();
                return await db.Memberships.AsNoTracking()
                    .Where(m => m.UserId == userId && m.Status == TenantMembershipStatus.Active)
                    .Join(db.Tenants.Where(t => t.Status == TenantStatus.Active || t.Status == TenantStatus.Suspended ||
                                                 t.Status == TenantStatus.Migrating),
                        m => m.TenantId, t => t.Id, (m, t) => new { t.Id, m.JoinedAt })
                    .OrderBy(x => x.JoinedAt)
                    .Select(x => x.Id)
                    .ToListAsync(ct);
            }
        }, cancellationToken);

    public async Task InvalidateAsync(Guid? tenantId = null, Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (tenantId is null)
            return;

        if (userId is null)
        {
            // Every membership of the tenant: bump its generation (old keys simply stop being read).
            var next = Guid.NewGuid().ToString("N")[..8];
            memoryCache.Set(GenerationPrefix + tenantId.Value.ToString("N"), next, GenerationTtl);
            await TryWriteAsync(GenerationPrefix + tenantId.Value.ToString("N"), next, cancellationToken, GenerationTtl);
            // Other instances drop their L1 generation and re-read the new one from L2.
            await invalidationBus.PublishAsync([GenerationPrefix + tenantId.Value.ToString("N")], cancellationToken);
            return;
        }

        var generation = await GetGenerationAsync(tenantId.Value, cancellationToken);
        var key = $"{Prefix}{tenantId:N}:{userId:N}:{generation}";
        memoryCache.Remove(key);
        try
        {
            await distributedCache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not invalidate cached membership {Key}", key);
        }

        // Broadcast AFTER the L2 removal, so a peer re-reading on receipt cannot re-cache the stale entry.
        await invalidationBus.PublishAsync([key], cancellationToken);
    }

    private static readonly TimeSpan GenerationTtl = TimeSpan.FromDays(30);

    private async Task<string> GetGenerationAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var key = GenerationPrefix + tenantId.ToString("N");
        if (memoryCache.TryGetValue(key, out string? local) && local is not null)
            return local;

        var generation = await TryReadAsync<string>(key, cancellationToken) ?? "0";
        if (options.Value.Cache.LocalSeconds > 0)
        {
            memoryCache.Set(key, generation, TimeSpan.FromSeconds(options.Value.Cache.LocalSeconds));
        }

        return generation;
    }

    private Task<CachedMembership> LoadAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        runner.RunAsync(async (services, ct) =>
        {
            using (TenantScope.BeginHostScope())
            {
                var db = services.GetRequiredService<TenancyDbContext>();
                var membership = await db.Memberships.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId &&
                                              m.Status == TenantMembershipStatus.Active, ct);
                if (membership is null)
                {
                    return CachedMembership.None;
                }

                var roles = await db.Roles.AsNoTracking()
                    .Where(r => r.TenantId == tenantId && membership.RoleIds.Contains(r.Id))
                    .ToListAsync(ct);

                var permissions = membership.IsOwner
                    ? TenantPermissions.All.ToArray()
                    : roles.SelectMany(r => r.EffectivePermissions).Distinct(StringComparer.Ordinal).ToArray();

                Guid[]? allowed = null;
                if (!membership.IsOwner && !permissions.Contains(TenantPermissions.BranchesAll))
                {
                    allowed = await ExpandBranchesAsync(db, tenantId, membership.Branches, ct);
                }

                return new CachedMembership(true, tenantId, userId, membership.IsOwner, permissions, allowed,
                    membership.Stamp);
            }
        }, cancellationToken);

    /// <summary>Assigned branches plus (when requested) every descendant, via materialized-path prefixes.</summary>
    internal static async Task<Guid[]> ExpandBranchesAsync(TenancyDbContext db, Guid tenantId,
        IReadOnlyCollection<BranchAssignment> assignments, CancellationToken ct)
    {
        if (assignments.Count == 0)
            return [];

        var assignedIds = assignments.Select(a => a.BranchId).ToList();
        var assigned = await db.Branches.AsNoTracking()
            .Where(b => b.TenantId == tenantId && assignedIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Path })
            .ToListAsync(ct);

        var result = new HashSet<Guid>(assigned.Select(b => b.Id));
        foreach (var branch in assigned.Where(b => assignments.Any(a => a.BranchId == b.Id && a.IncludeDescendants)))
        {
            var prefix = branch.Path;
            var descendants = await db.Branches.AsNoTracking()
                .Where(b => b.TenantId == tenantId && b.Path.StartsWith(prefix))
                .Select(b => b.Id)
                .ToListAsync(ct);
            result.UnionWith(descendants);
        }

        return result.ToArray();
    }

    private async Task<T?> TryReadAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var json = await distributedCache.GetStringAsync(key, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading membership cache {Key} failed; using the database", key);
            return null;
        }
    }

    private async Task TryWriteAsync<T>(string key, T value, CancellationToken cancellationToken, TimeSpan? ttl = null)
    {
        var seconds = options.Value.Cache.DistributedSeconds;
        if (seconds <= 0 && ttl is null)
            return;
        try
        {
            await distributedCache.SetStringAsync(key, JsonSerializer.Serialize(value, Json),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromSeconds(seconds)
                }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Writing membership cache {Key} failed", key);
        }
    }

    /// <summary>Serializable membership snapshot (<see cref="Exists"/> = false caches "not a member").</summary>
    private sealed record CachedMembership(
        bool Exists,
        Guid TenantId,
        Guid UserId,
        bool IsOwner,
        string[] Permissions,
        Guid[]? AllowedBranchIds,
        string Stamp)
    {
        public static CachedMembership None { get; } = new(false, Guid.Empty, Guid.Empty, false, [], [], string.Empty);

        public TenantMembershipInfo? ToInfo() => Exists
            ? new TenantMembershipInfo(TenantId, UserId, IsOwner,
                new HashSet<string>(Permissions, StringComparer.Ordinal),
                AllowedBranchIds is null ? BranchScope.Unrestricted : new BranchScope(AllowedBranchIds),
                Stamp)
            : null;
    }
}
