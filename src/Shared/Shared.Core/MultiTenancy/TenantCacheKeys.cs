namespace Shared.Core.MultiTenancy;

/// <summary>
/// Builds cache keys for TENANT data: <c>t:{tenantId}:{key}</c>. Every cache entry derived from tenant data must use
/// it - a global key would serve one tenant's data to another. Throws when no tenant is resolved (fail closed).
/// User-keyed or platform-wide entries (token blacklist, OTP codes, plan catalog) keep plain keys.
/// </summary>
public interface ITenantCacheKeys
{
    string For(string key);

    /// <summary>Prefix of every key of <paramref name="tenantId"/> (bulk invalidation on purge).</summary>
    string PrefixOf(Guid tenantId);
}

public sealed class TenantCacheKeys(ITenantContext tenantContext) : ITenantCacheKeys
{
    public string For(string key)
    {
        var tenantId = tenantContext.TenantId
                       ?? throw new TenantNotResolvedException("tenant-scoped cache access without a tenant.");
        return PrefixOf(tenantId) + key;
    }

    public string PrefixOf(Guid tenantId) => $"t:{tenantId:N}:";
}
