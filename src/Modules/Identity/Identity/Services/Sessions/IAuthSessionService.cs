using Shared.Core.MultiTenancy;
namespace Identity.Services.Sessions;

/// <summary>
/// The only place that creates sessions (access token + persisted, rotating refresh token).
/// Every token-issuing handler (login, register, onboarding verify, external login, change password, refresh)
/// goes through it, so a refresh token is never returned without being stored.
/// </summary>
public interface IAuthSessionService
{
    /// <summary>Starts a new session (new refresh-token family) for <paramref name="user"/>.</summary>
    Task<AuthSession> IssueAsync(ApplicationUser user, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Starts a new session. <paramref name="multiFactor"/> = the user just passed a second factor: the access token
    /// carries <c>amr=mfa</c> and so do all tokens refreshed from this session.
    /// </summary>
    Task<AuthSession> IssueAsync(ApplicationUser user, string? ipAddress, bool multiFactor, CancellationToken ct = default);

    /// <summary>
    /// Rotates a refresh token. Rejects unknown/expired/revoked tokens and users that are deleted, locked,
    /// inactive or whose security stamp changed. Re-use of a revoked token, or losing a concurrent rotation,
    /// revokes the whole token family. Throws <see cref="UnauthorizedException"/> on rejection.
    /// </summary>
    Task<AuthSession> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Starts a new session bound to <paramref name="tenantId"/> (multi-tenancy tenant switch). The user must be an
    /// active member of a serviceable tenant; otherwise <see cref="TenantAccessDeniedException"/> (403).
    /// </summary>
    Task<AuthSession> SwitchTenantAsync(ApplicationUser user, Guid tenantId, string? ipAddress, bool multiFactor,
        CancellationToken ct = default);
}

public sealed record AuthSession(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    IReadOnlyList<string> Roles,
    bool MultiFactor = false,
    Guid? TenantId = null);
