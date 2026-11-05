using Identity.Models;

namespace Identity.Data.Repository.ApiKeys;

public interface IApiKeyRepository
{
    Task AddAsync(ApiKey key, CancellationToken ct = default);

    /// <summary>Untracked lookup by the public prefix (authentication hot path).</summary>
    Task<ApiKey?> FindByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>Tracked lookup scoped to the owner; another user's key id finds nothing.</summary>
    Task<ApiKey?> FindOwnedAsync(Guid userId, Guid keyId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Sets <c>last_used_at</c> without loading the row.</summary>
    Task TouchAsync(Guid keyId, DateTime nowUtc, CancellationToken ct = default);
}
