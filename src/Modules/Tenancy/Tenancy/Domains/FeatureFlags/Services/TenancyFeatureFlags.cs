using Shared.Contracts.FeatureFlags;
using Shared.Core.FeatureFlags;
using Tenancy.Domains.FeatureFlags.Models;

namespace Tenancy.Domains.FeatureFlags.Services;

/// <summary>
/// Database-backed <see cref="IFeatureFlags"/> (replaces the configuration-only default). Evaluates the cached snapshot
/// for the ambient tenant and user with <see cref="FeatureFlagEvaluator"/>; flags that are not stored fall back to
/// <c>FeatureFlags:Defaults</c>.
/// </summary>
public sealed class TenancyFeatureFlags(
    FeatureFlagStore store,
    FeatureFlagDefaults defaults,
    ITenantContext tenantContext,
    IHttpContextService httpContext) : IFeatureFlags
{
    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        var normalized = FeatureFlag.NormalizeKey(key);
        var snapshot = await store.GetSnapshotAsync(cancellationToken);
        snapshot.TryGetValue(normalized, out var flag);
        return FeatureFlagEvaluator.Evaluate(flag, tenantContext.TenantId, httpContext.GetUserIdAsGuid(),
            defaults.Get(normalized));
    }

    public async Task<IReadOnlyDictionary<string, bool>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await store.GetSnapshotAsync(cancellationToken);
        var configured = defaults.All();
        var tenantId = tenantContext.TenantId;
        var userId = httpContext.GetUserIdAsGuid();

        var result = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (key, value) in configured)
            result[FeatureFlag.NormalizeKey(key)] = value;
        foreach (var (key, flag) in snapshot)
            result[key] = FeatureFlagEvaluator.Evaluate(flag, tenantId, userId, configuredDefault: false);

        // Only what is ON for this caller: a dark-launched (off) flag's key must not leak to the client. Absent = off.
        foreach (var key in result.Where(x => !x.Value).Select(x => x.Key).ToList())
            result.Remove(key);
        return result;
    }
}
