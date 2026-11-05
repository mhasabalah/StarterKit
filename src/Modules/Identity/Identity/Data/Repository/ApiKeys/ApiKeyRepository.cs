using Identity.Models;

namespace Identity.Data.Repository.ApiKeys;

public class ApiKeyRepository(ApplicationIdentityDbContext dbContext) : IApiKeyRepository
{
    public async Task AddAsync(ApiKey key, CancellationToken ct = default)
    {
        dbContext.ApiKeys.Add(key);
        await dbContext.SaveChangesAsync(ct);
    }

    public Task<ApiKey?> FindByPrefixAsync(string prefix, CancellationToken ct = default)
        => dbContext.ApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Prefix == prefix, ct);

    public Task<ApiKey?> FindOwnedAsync(Guid userId, Guid keyId, CancellationToken ct = default)
        => dbContext.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.UserId == userId, ct);

    public Task<int> CountActiveAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default)
        => dbContext.ApiKeys.CountAsync(k => k.UserId == userId && k.RevokedAt == null && k.ExpiresAt > nowUtc, ct);

    public async Task SaveAsync(CancellationToken ct = default) => await dbContext.SaveChangesAsync(ct);

    public Task TouchAsync(Guid keyId, DateTime nowUtc, CancellationToken ct = default)
        => dbContext.ApiKeys
            .Where(k => k.Id == keyId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, nowUtc), ct);
}
