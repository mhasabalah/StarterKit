using Identity.Data.Repository.RefreshTokens;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using RefreshTokenEntity = Identity.Models.RefreshToken;

namespace Identity.Services.Sessions;

public sealed class AuthSessionService(
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator tokenGenerator,
    IRefreshTokenRepository refreshTokens,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<AuthSessionService> logger,
    ITenantMembershipResolver? memberships = null,
    ITenantStore? tenantStore = null,
    ITenantContext? tenantContext = null,
    IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
    IHttpContextService? httpContext = null) : IAuthSessionService
{
    private bool MultiTenancyEnabled => multiTenancyOptions?.Value.Enabled == true && memberships is not null;

    public const string InvalidRefreshTokenMessage = "Invalid or expired refresh token.";
    public const string ReuseDetectedReason = "Refresh token reuse detected";

    private readonly JwtOptions _jwt = jwtOptions.Value;

    public Task<AuthSession> IssueAsync(ApplicationUser user, string? ipAddress, CancellationToken ct = default) =>
        IssueAsync(user, ipAddress, multiFactor: false, ct);

    public async Task<AuthSession> IssueAsync(
        ApplicationUser user, string? ipAddress, bool multiFactor, CancellationToken ct = default)
    {
        var membership = await ChooseLoginTenantAsync(user, ct);
        var (session, token) = await CreateSessionAsync(user, familyId: Guid.NewGuid(), ipAddress, multiFactor, membership,
            DeviceNameParser.Truncate(httpContext?.GetUserAgent()));
        await refreshTokens.AddAsync(token, ct);
        await RememberTenantAsync(user, membership);
        return session;
    }

    public async Task<AuthSession> SwitchTenantAsync(ApplicationUser user, Guid tenantId, string? ipAddress,
        bool multiFactor, CancellationToken ct = default)
    {
        if (!MultiTenancyEnabled)
            throw new TenantAccessDeniedException(TenantErrors.MultiTenancyDisabled, "multi-tenancy is disabled.");

        var tenant = await tenantStore!.FindByIdAsync(tenantId, ct);
        if (tenant is not { Status: TenantStatus.Active or TenantStatus.Suspended or TenantStatus.Migrating })
            throw new TenantAccessDeniedException(TenantErrors.TenantUnavailable, "this tenant is not available.");

        var membership = await memberships!.GetMembershipAsync(user.Id, tenantId, ct)
                         ?? throw new TenantAccessDeniedException(TenantErrors.NotAMember, "you are not a member of this tenant.");

        var (session, token) = await CreateSessionAsync(user, Guid.NewGuid(), ipAddress, multiFactor, membership,
            DeviceNameParser.Truncate(httpContext?.GetUserAgent()));
        await refreshTokens.AddAsync(token, ct);
        await RememberTenantAsync(user, membership);

        logger.LogInformation("User {UserId} switched to tenant {TenantId}", user.Id, tenantId);
        return session;
    }

    /// <summary>
    /// Login tenant: the tenant addressed by the request (host/header) if the user belongs to it, else the last active
    /// tenant, else the only tenant. Several tenants and no hint → a tenant-less session (client calls switch-tenant).
    /// </summary>
    private async Task<TenantMembershipInfo?> ChooseLoginTenantAsync(ApplicationUser user, CancellationToken ct)
    {
        if (!MultiTenancyEnabled)
            return null;

        foreach (var candidate in new[] { tenantContext?.TenantId, user.LastActiveTenantId })
        {
            if (candidate is { } id && await TryMembershipAsync(user.Id, id, ct) is { } membership)
                return membership;
        }

        var tenantIds = await memberships!.GetTenantIdsOfUserAsync(user.Id, ct);
        return tenantIds.Count == 1 ? await TryMembershipAsync(user.Id, tenantIds[0], ct) : null;
    }

    private async Task<TenantMembershipInfo?> TryMembershipAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var tenant = await tenantStore!.FindByIdAsync(tenantId, ct);
        if (tenant is not { Status: TenantStatus.Active or TenantStatus.Suspended or TenantStatus.Migrating })
            return null;
        return await memberships!.GetMembershipAsync(userId, tenantId, ct);
    }

    private async Task RememberTenantAsync(ApplicationUser user, TenantMembershipInfo? membership)
    {
        if (membership is null || user.LastActiveTenantId == membership.TenantId)
            return;
        user.LastActiveTenantId = membership.TenantId;
        await userManager.UpdateAsync(user);
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, string? ipAddress, CancellationToken ct = default)
    {
        var current = await refreshTokens.FindByHashAsync(TokenHasher.Hash(refreshToken), ct);
        if (current is null)
        {
            logger.LogWarning("Refresh rejected: unknown token");
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        if (current.RevokedAt is not null)
        {
            // A revoked token presented again means it leaked (or was replayed): kill every session descending
            // from the same login so the thief's rotated token dies too.
            await RevokeFamilyAsync(current, ipAddress, ct);
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (current.ExpiresAt <= now)
        {
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        var user = await userManager.FindByIdAsync(current.UserId.ToString());
        var rejection = user is null ? "user deleted" : await GetUserRejectionAsync(user, current);
        if (rejection is not null)
        {
            logger.LogWarning("Refresh rejected for user {UserId}: {Reason}", current.UserId, rejection);
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, $"Refresh rejected: {rejection}", ipAddress, ct);
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        // The session keeps its tenant while the user is still a member; otherwise it continues tenant-less.
        var membership = current.TenantId is { } tenantId && MultiTenancyEnabled
            ? await TryMembershipAsync(user!.Id, tenantId, ct)
            : null;

        var (session, replacement) =
            await CreateSessionAsync(user!, current.FamilyId, ipAddress, current.MultiFactor, membership,
                current.UserAgent);

        if (!await refreshTokens.TryRotateAsync(current.Id, replacement, ipAddress, ct))
        {
            // Lost the race against another refresh with the same token: treat as reuse.
            await RevokeFamilyAsync(current, ipAddress, ct);
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        logger.LogInformation("Refresh token rotated for user {UserId}", current.UserId);
        return session;
    }

    private async Task<string?> GetUserRejectionAsync(ApplicationUser user, RefreshTokenEntity token)
    {
        if (!user.IsActive)
        {
            return "user inactive";
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return "user locked out";
        }

        return string.Equals(
            TokenHasher.HashSecurityStamp(user.SecurityStamp), token.SecurityStampHash, StringComparison.Ordinal)
            ? null
            : "security stamp changed";
    }

    private async Task RevokeFamilyAsync(RefreshTokenEntity token, string? ipAddress, CancellationToken ct)
    {
        var revoked = await refreshTokens.RevokeFamilyAsync(token.FamilyId, ReuseDetectedReason, ipAddress, ct);
        logger.LogWarning(
            "Refresh token reuse detected for user {UserId}; revoked {Count} tokens of family {FamilyId}",
            token.UserId, revoked, token.FamilyId);
    }

    private async Task<(AuthSession Session, RefreshTokenEntity Token)> CreateSessionAsync(
        ApplicationUser user, Guid familyId, string? ipAddress, bool multiFactor, TenantMembershipInfo? membership = null,
        string? userAgent = null)
    {
        var roles = (await userManager.GetRolesAsync(user)).ToList();
        var accessToken = await tokenGenerator.GenerateTokenAsync(
            user, roles, multiFactor, membership, impersonation: null, sessionId: familyId);
        var rawRefreshToken = tokenGenerator.GenerateRefreshToken();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var token = new RefreshTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = TokenHasher.Hash(rawRefreshToken),
            SecurityStampHash = TokenHasher.HashSecurityStamp(user.SecurityStamp),
            MultiFactor = multiFactor,
            TenantId = membership?.TenantId,
            CreatedAt = now,
            LastUsedAt = now,
            UserAgent = userAgent,
            DeviceName = DeviceNameParser.Parse(userAgent),
            ExpiresAt = now.AddDays(_jwt.RefreshTokenExpirationDays),
            CreatedByIp = Truncate(ipAddress ?? "unknown", 45)
        };

        var session = new AuthSession(
            accessToken,
            rawRefreshToken,
            now.AddMinutes(_jwt.AccessTokenExpirationMinutes),
            roles,
            multiFactor,
            membership?.TenantId);

        return (session, token);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
