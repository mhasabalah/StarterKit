namespace Identity.Services.Token;

/// <summary>
/// Redis-backed revocation of access tokens (JWTs are stateless, so revocation needs a lookup).
/// Refresh tokens are revoked in the database (<see cref="Data.Repository.RefreshTokens.IRefreshTokenRepository"/>).
/// </summary>
public interface ITokenBlacklistService
{
    /// <summary>Revoke one access token (logout) until it expires.</summary>
    Task BlacklistTokenAsync(string token, DateTime expiresAt);

    Task<bool> IsTokenBlacklistedAsync(string token);

    /// <summary>
    /// Revoke every access token of a user that was issued before the user's security stamp changed to
    /// <paramref name="currentSecurityStampHash"/>. Tokens issued afterwards (with the new stamp) stay valid,
    /// so the user can sign in again immediately (e.g. after a password reset).
    /// </summary>
    Task BlacklistUserAsync(Guid userId, string currentSecurityStampHash, TimeSpan duration);

    /// <summary>True when the user's tokens were revoked and this token carries an older security stamp.</summary>
    Task<bool> IsUserTokenRevokedAsync(Guid userId, string? tokenSecurityStampHash);

    /// <summary>Revoke every access token of one session (<c>sid</c> claim = refresh-token family) for <paramref name="duration"/>.</summary>
    Task BlacklistSessionAsync(Guid sessionId, TimeSpan duration);

    /// <summary>True when the session's access tokens were revoked by <see cref="BlacklistSessionAsync"/>.</summary>
    Task<bool> IsSessionRevokedAsync(Guid sessionId);
}
