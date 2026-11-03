using Identity.Models;
using Identity.Services.Otp;

namespace Identity.Data.Repository.Otp;

public class OtpCodeRepository(ApplicationIdentityDbContext dbContext) : IOtpCodeRepository
{
    public Task<OtpCode?> GetAsync(Guid userId, OtpPurpose purpose, CancellationToken ct = default)
        => dbContext.OtpCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Purpose == purpose, ct);

    public async Task<bool> TryIssueAsync(
        OtpCode code, DateTime cooldownBoundary, DateTime windowBoundary, int maxSendsPerWindow,
        CancellationToken ct = default)
    {
        if (await TryReplaceAsync(code, cooldownBoundary, windowBoundary, maxSendsPerWindow, ct))
        {
            return true;
        }

        if (await dbContext.OtpCodes.AnyAsync(x => x.UserId == code.UserId && x.Purpose == code.Purpose, ct))
        {
            return false; // the row exists, so the gate refused
        }

        // First code ever for this user + purpose. A parallel first issue loses on the primary key and then goes
        // through the same conditional UPDATE as everyone else.
        dbContext.OtpCodes.Add(code);
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            dbContext.Entry(code).State = EntityState.Detached;
            return await TryReplaceAsync(code, cooldownBoundary, windowBoundary, maxSendsPerWindow, ct);
        }
        finally
        {
            dbContext.Entry(code).State = EntityState.Detached;
        }
    }

    /// <summary>One UPDATE; PostgreSQL re-checks the WHERE on the latest row version, so the gate is race free.</summary>
    private async Task<bool> TryReplaceAsync(
        OtpCode code, DateTime cooldownBoundary, DateTime windowBoundary, int maxSendsPerWindow, CancellationToken ct)
    {
        var updated = await dbContext.OtpCodes
            .Where(x => x.UserId == code.UserId
                        && x.Purpose == code.Purpose
                        && x.LastSentAt <= cooldownBoundary
                        && (x.WindowStartedAt <= windowBoundary || x.SendCount < maxSendsPerWindow))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CodeHash, code.CodeHash)
                .SetProperty(x => x.ExpiresAt, code.ExpiresAt)
                .SetProperty(x => x.Attempts, 0)
                .SetProperty(x => x.MaxAttempts, code.MaxAttempts)
                .SetProperty(x => x.LastSentAt, code.LastSentAt)
                .SetProperty(x => x.WindowStartedAt,
                    x => x.WindowStartedAt <= windowBoundary ? code.WindowStartedAt : x.WindowStartedAt)
                .SetProperty(x => x.SendCount,
                    x => x.WindowStartedAt <= windowBoundary ? 1 : x.SendCount + 1), ct);

        return updated == 1;
    }

    public async Task<bool> TryConsumeAttemptAsync(
        Guid userId, OtpPurpose purpose, string codeHash, CancellationToken ct = default)
    {
        var updated = await dbContext.OtpCodes
            .Where(x => x.UserId == userId
                        && x.Purpose == purpose
                        && x.CodeHash == codeHash
                        && x.Attempts < x.MaxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);

        return updated == 1;
    }

    public async Task<bool> TryConsumeAsync(
        Guid userId, OtpPurpose purpose, string codeHash, CancellationToken ct = default)
    {
        var updated = await dbContext.OtpCodes
            .Where(x => x.UserId == userId && x.Purpose == purpose && x.CodeHash == codeHash)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CodeHash, string.Empty), ct);

        return updated == 1;
    }

    public async Task InvalidateAsync(
        Guid userId, OtpPurpose purpose, string? onlyIfCodeHash, DateTime? lastSentAt, CancellationToken ct = default)
    {
        var query = dbContext.OtpCodes.Where(x => x.UserId == userId && x.Purpose == purpose && x.CodeHash != "");
        if (onlyIfCodeHash is not null)
        {
            query = query.Where(x => x.CodeHash == onlyIfCodeHash);
        }

        if (lastSentAt is { } rewound)
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CodeHash, string.Empty)
                .SetProperty(x => x.LastSentAt, rewound), ct);
        }
        else
        {
            await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.CodeHash, string.Empty), ct);
        }
    }
}
