using Identity.Models;

namespace Identity.Data.Repository.RefreshTokens;

public interface IRefreshTokenRepository
{
    /// <summary>Untracked lookup by token hash.</summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>
    /// Atomically rotates <paramref name="currentTokenId"/> into <paramref name="replacement"/> in one transaction:
    /// the old row is revoked with a conditional <c>UPDATE … WHERE revoked_at IS NULL</c> and the new row is inserted.
    /// Returns false (and inserts nothing) when the old token was already revoked, e.g. by a concurrent refresh.
    /// </summary>
    Task<bool> TryRotateAsync(
        Guid currentTokenId, RefreshToken replacement, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revokes one active token of a user. Returns false when it was not active.</summary>
    Task<bool> RevokeAsync(Guid userId, string tokenHash, string reason, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revokes every active token of a token family (reuse detection).</summary>
    Task<int> RevokeFamilyAsync(Guid familyId, string reason, string? ipAddress, CancellationToken ct = default);

    /// <summary>Revokes every active token of a user (all sessions).</summary>
    Task<int> RevokeAllForUserAsync(Guid userId, string reason, string? ipAddress, CancellationToken ct = default);

    /// <summary>Deletes expired tokens in batches; returns the number deleted.</summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);
}
