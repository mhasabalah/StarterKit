using Identity.Models;

namespace Identity.Data.Repository.RefreshTokens;

/// <summary>Read model of one active session.</summary>
public sealed record ActiveSession(
    Guid FamilyId,
    string? DeviceName,
    string? UserAgent,
    string IpAddress,
    DateTime SignedInAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    Guid? TenantId);

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

    /// <summary>
    /// One row per active session (token family with a live, unrevoked token), newest activity first.
    /// <see cref="ActiveSession.SignedInAt"/> is the creation time of the family's first token.
    /// </summary>
    Task<List<ActiveSession>> ListActiveFamiliesAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>
    /// Revokes the family <paramref name="familyId"/> ONLY if it belongs to <paramref name="userId"/> (a session id
    /// of someone else revokes nothing). Returns the number of tokens revoked; 0 = unknown, foreign or already ended.
    /// </summary>
    Task<int> RevokeUserFamilyAsync(
        Guid userId, Guid familyId, string reason, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Revokes every active session of the user except <paramref name="exceptFamilyId"/> and returns the ids of the
    /// ended sessions (so their access tokens can be blacklisted).
    /// </summary>
    Task<List<Guid>> RevokeOtherFamiliesAsync(
        Guid userId, Guid? exceptFamilyId, string reason, string? ipAddress, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Deletes expired tokens in batches; returns the number deleted.</summary>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);
}
