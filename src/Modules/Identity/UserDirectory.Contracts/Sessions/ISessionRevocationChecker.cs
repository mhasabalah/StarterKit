namespace UserDirectory.Contracts.Sessions;

/// <summary>
/// Port: is this access token's session revoked? Implemented by the identity provider module (cheap store lookups),
/// consumed by long-lived connections (the notification hub) that must re-check after the handshake. When no
/// implementation is registered the check is skipped. Fails by throwing; callers decide (the hub fails open).
/// </summary>
public interface ISessionRevocationChecker
{
    /// <param name="userId">The token's user.</param>
    /// <param name="sessionId">The token's <c>sid</c> claim, if any.</param>
    /// <param name="securityStampHash">The token's security-stamp hash claim: a stamp rotation (revoke-all) invalidates older tokens.</param>
    Task<bool> IsRevokedAsync(Guid userId, Guid? sessionId, string? securityStampHash, CancellationToken ct = default);
}
