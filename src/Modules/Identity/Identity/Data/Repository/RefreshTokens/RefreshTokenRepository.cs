using Identity.Models;

namespace Identity.Data.Repository.RefreshTokens;

public class RefreshTokenRepository(ApplicationIdentityDbContext dbContext) : IRefreshTokenRepository
{
    private const int MaxIpLength = 45;
    private const int MaxReasonLength = 200;

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
        => dbContext.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        dbContext.RefreshTokens.Add(token);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<bool> TryRotateAsync(
        Guid currentTokenId, RefreshToken replacement, string? ipAddress, CancellationToken ct = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        var ip = Truncate(ipAddress, MaxIpLength);

        // Conditional update = the lock. Of two concurrent refreshes with the same token only one matches
        // "revoked_at IS NULL"; the other gets 0 rows and must not issue a new token.
        var claimed = await dbContext.RefreshTokens
            .Where(t => t.Id == currentTokenId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedByIp, ip)
                .SetProperty(t => t.RevokedReason, RefreshToken.RotatedReason)
                .SetProperty(t => t.ReplacedByTokenId, replacement.Id), ct);

        if (claimed != 1)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        dbContext.RefreshTokens.Add(replacement);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> RevokeAsync(
        Guid userId, string tokenHash, string reason, string? ipAddress, CancellationToken ct = default)
    {
        var revoked = await RevokeWhere(
            dbContext.RefreshTokens.Where(t => t.UserId == userId && t.TokenHash == tokenHash),
            reason, ipAddress, ct);

        return revoked == 1;
    }

    public Task<int> RevokeFamilyAsync(Guid familyId, string reason, string? ipAddress, CancellationToken ct = default)
        => RevokeWhere(dbContext.RefreshTokens.Where(t => t.FamilyId == familyId), reason, ipAddress, ct);

    public Task<int> RevokeAllForUserAsync(Guid userId, string reason, string? ipAddress, CancellationToken ct = default)
        => RevokeWhere(dbContext.RefreshTokens.Where(t => t.UserId == userId), reason, ipAddress, ct);

    public async Task<List<ActiveSession>> ListActiveFamiliesAsync(
        Guid userId, DateTime nowUtc, CancellationToken ct = default)
    {
        var live = await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > nowUtc)
            .Select(t => new
            {
                t.FamilyId, t.DeviceName, t.UserAgent, t.CreatedByIp, t.CreatedAt, t.LastUsedAt, t.ExpiresAt, t.TenantId
            })
            .ToListAsync(ct);

        if (live.Count == 0) return [];

        var familyIds = live.Select(t => t.FamilyId).Distinct().ToList();
        var starts = await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && familyIds.Contains(t.FamilyId))
            .GroupBy(t => t.FamilyId)
            .Select(g => new { FamilyId = g.Key, SignedInAt = g.Min(t => t.CreatedAt) })
            .ToDictionaryAsync(x => x.FamilyId, x => x.SignedInAt, ct);

        return live
            .GroupBy(t => t.FamilyId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).First())
            .Select(t => new ActiveSession(
                t.FamilyId, t.DeviceName, t.UserAgent, t.CreatedByIp,
                starts.GetValueOrDefault(t.FamilyId, t.CreatedAt),
                t.LastUsedAt ?? t.CreatedAt, t.ExpiresAt, t.TenantId))
            .OrderByDescending(s => s.LastUsedAt)
            .ToList();
    }

    public Task<int> RevokeUserFamilyAsync(
        Guid userId, Guid familyId, string reason, string? ipAddress, CancellationToken ct = default)
        => RevokeWhere(
            dbContext.RefreshTokens.Where(t => t.UserId == userId && t.FamilyId == familyId),
            reason, ipAddress, ct);

    public async Task<List<Guid>> RevokeOtherFamiliesAsync(
        Guid userId, Guid? exceptFamilyId, string reason, string? ipAddress, DateTime nowUtc, CancellationToken ct = default)
    {
        var familyIds = await dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > nowUtc
                        && (exceptFamilyId == null || t.FamilyId != exceptFamilyId))
            .Select(t => t.FamilyId)
            .Distinct()
            .ToListAsync(ct);

        if (familyIds.Count == 0) return [];

        await RevokeWhere(
            dbContext.RefreshTokens.Where(t => t.UserId == userId && familyIds.Contains(t.FamilyId)),
            reason, ipAddress, ct);

        return familyIds;
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default)
    {
        var total = 0;
        int deleted;

        do
        {
            // DELETE … WHERE id IN (SELECT id … LIMIT n): short transactions, bounded memory, no entity loading.
            var batchIds = dbContext.RefreshTokens
                .Where(t => t.ExpiresAt < cutoffUtc)
                .OrderBy(t => t.ExpiresAt)
                .Select(t => t.Id)
                .Take(batchSize);

            deleted = await dbContext.RefreshTokens
                .Where(t => batchIds.Contains(t.Id))
                .ExecuteDeleteAsync(ct);

            total += deleted;
        } while (deleted == batchSize);

        return total;
    }

    private static Task<int> RevokeWhere(
        IQueryable<RefreshToken> tokens, string reason, string? ipAddress, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var ip = Truncate(ipAddress, MaxIpLength);
        var why = Truncate(reason, MaxReasonLength);

        return tokens
            .Where(t => t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedByIp, ip)
                .SetProperty(t => t.RevokedReason, why), ct);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
