namespace Shared.Core.Authorization;

/// <summary>
/// Port: has a sign-in session (the <c>sid</c> claim) been revoked? Implemented by the identity provider module (cheap
/// store lookup), consumed by long-lived connections (notification hub) that must re-check after the handshake. When
/// no implementation is registered the check is skipped.
/// </summary>
public interface ISessionRevocationChecker
{
    Task<bool> IsRevokedAsync(Guid sessionId, CancellationToken ct = default);
}
