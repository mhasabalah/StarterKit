using System.Security.Claims;
using Identity.Data.Repository.ApiKeys;
using Identity.Models;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.Services.ApiKeys;

public sealed record ApiKeyAuthResult(ClaimsPrincipal? Principal, string? Failure)
{
    public bool Succeeded => Principal is not null;
    public static ApiKeyAuthResult Ok(ClaimsPrincipal principal) => new(principal, null);
    public static ApiKeyAuthResult Fail(string reason) => new(null, reason);
}

/// <summary>Validates a presented API key and builds the principal of its owner (limited by the key's scopes).</summary>
public interface IApiKeyAuthenticator
{
    Task<ApiKeyAuthResult> AuthenticateAsync(string rawKey, CancellationToken ct = default);
}

public sealed class ApiKeyAuthenticator(
    IApiKeyRepository keys,
    UserManager<ApplicationUser> userManager,
    IOptions<ApiKeyOptions> options,
    TimeProvider timeProvider,
    ILogger<ApiKeyAuthenticator> logger,
    ITenantMembershipResolver? memberships = null,
    IOptions<MultiTenancyOptions>? multiTenancyOptions = null) : IApiKeyAuthenticator
{
    public const string AuthenticationType = "ApiKey";

    // Compared against when the prefix is unknown, so unknown and wrong keys cost about the same.
    private static readonly ApiKey DummyKey = ApiKey.Create(
        Guid.Empty, null, "dummy", [], DateTime.UtcNow, DateTime.UtcNow).Key;

    public async Task<ApiKeyAuthResult> AuthenticateAsync(string rawKey, CancellationToken ct = default)
    {
        if (!options.Value.Enabled) return ApiKeyAuthResult.Fail("API keys are disabled.");
        if (!ApiKey.TryParsePrefix(rawKey, out var prefix)) return ApiKeyAuthResult.Fail("Invalid API key.");

        var key = await keys.FindByPrefixAsync(prefix, ct);
        if (key is null)
        {
            _ = DummyKey.Matches(rawKey);
            return ApiKeyAuthResult.Fail("Invalid API key.");
        }

        if (!key.Matches(rawKey)) return ApiKeyAuthResult.Fail("Invalid API key.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (key.IsRevoked) return ApiKeyAuthResult.Fail("API key has been revoked.");
        if (key.IsExpired(now)) return ApiKeyAuthResult.Fail("API key has expired.");

        var user = await userManager.FindByIdAsync(key.UserId.ToString());
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("API key {KeyId} rejected: owner {UserId} is inactive, locked or deleted", key.Id, key.UserId);
            return ApiKeyAuthResult.Fail("API key owner is not active.");
        }

        TenantMembershipInfo? membership = null;
        if (key.TenantId is { } tenantId && multiTenancyOptions?.Value.Enabled == true && memberships is not null)
        {
            membership = await memberships.GetMembershipAsync(user.Id, tenantId, ct);
            if (membership is null)
            {
                logger.LogWarning("API key {KeyId} rejected: owner {UserId} is no longer a member of {TenantId}",
                    key.Id, user.Id, tenantId);
                return ApiKeyAuthResult.Fail("API key owner is no longer a member of the tenant.");
            }
        }

        await TouchThrottledAsync(key, now, ct);

        var roles = await userManager.GetRolesAsync(user);
        return ApiKeyAuthResult.Ok(BuildPrincipal(key, user, roles, membership));
    }

    private async Task TouchThrottledAsync(ApiKey key, DateTime now, CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(0, options.Value.LastUsedWriteIntervalMinutes));
        if (key.LastUsedAt is { } last && now - last < interval) return;

        try
        {
            await keys.TouchAsync(key.Id, now, ct);
        }
        catch (Exception ex)
        {
            // Bookkeeping only; never fail a valid request because of it.
            logger.LogWarning(ex, "Failed to update last_used_at of API key {KeyId}", key.Id);
        }
    }

    internal static ClaimsPrincipal BuildPrincipal(
        ApiKey key, ApplicationUser user, IEnumerable<string> roles, TenantMembershipInfo? membership)
    {
        var roleList = roles.ToList();
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? ""),
            new("full_name", user.FullName),
            new("phone_verified", user.IsPhoneNumberVerified.ToString()),
            new("email_verified", user.EmailConfirmed.ToString()),
            new("profile_complete", user.IsProfileComplete.ToString()),
            new(IdentityClaimTypes.MustChangePassword, user.MustChangePasswordOnNextLogin ? "true" : "false"),
            new(IdentityClaimTypes.SecurityStampHash, TokenHasher.HashSecurityStamp(user.SecurityStamp)),
            new(ApiKeyClaimTypes.KeyId, key.Id.ToString())
        };

        // NO ClaimTypes.Role and NO is_admin: scopes must not be bypassable by role checks (IsInRole, AdminOnly, ...).
        claims.AddRange(roleList.Distinct(StringComparer.Ordinal).Select(r => new Claim(ApiKeyClaimTypes.OwnerRole, r)));
        claims.AddRange(key.Scopes.Select(s => new Claim(ApiKeyClaimTypes.Scope, s)));

        if (membership is not null)
        {
            claims.Add(new Claim(TenantClaimTypes.TenantId, membership.TenantId.ToString()));
            claims.Add(new Claim(TenantClaimTypes.MembershipStamp, membership.Stamp));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, ClaimTypes.Name, ClaimTypes.Role));
    }
}
