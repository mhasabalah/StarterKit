using Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;

namespace Identity.Authorization;

/// <summary>The caller's token must carry the claim <c>permission = Permission</c>.</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;

    public override string ToString() => $"{nameof(PermissionRequirement)}: {Permission}";
}

/// <summary>
/// Callers in one of <see cref="TwoFactorOptions.RequiredForRoles"/> must present an MFA session
/// (<c>amr=mfa</c>). Part of every <c>perm:*</c> policy and of the AdminOnly / SuperAdminOnly policies.
/// </summary>
public sealed class MfaRequirement : IAuthorizationRequirement
{
    public static readonly MfaRequirement Instance = new();

    private MfaRequirement()
    {
    }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Decides <see cref="MfaRequirement"/> from the token alone (roles + amr claims), so it costs no I/O.
/// <list type="bullet">
/// <item>Caller has no role from RequiredForRoles, or already has an MFA session: succeed.</item>
/// <item>Enforce mode: leave the requirement unmet (403; see <see cref="MfaAwareAuthorizationResultHandler"/> for the
/// <c>mfa_required</c> body). It deliberately does not call <c>context.Fail()</c>: that would hide which OTHER
/// requirements (e.g. the permission) are unmet, and the response must only say mfa_required when MFA is the sole
/// reason.</item>
/// <item>Warn mode: succeed, log a warning and add the <see cref="AuthorizationErrorCodes.HeaderName"/> header.</item>
/// </list>
/// </summary>
public sealed class MfaAuthorizationHandler(
    IOptionsMonitor<TwoFactorOptions> options,
    ILogger<MfaAuthorizationHandler> logger) : AuthorizationHandler<MfaRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaRequirement requirement)
    {
        var settings = options.CurrentValue;

        if (!RequiresMfa(context.User, settings.EffectiveRequiredForRoles) ||
            AuthenticationMethods.IsMultiFactor(context.User))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (settings.EffectiveEnforcementMode == TwoFactorEnforcementMode.Enforce)
        {
            logger.LogWarning("Privileged request without an MFA session rejected for user {UserId}", userId);
            return Task.CompletedTask;
        }

        logger.LogWarning(
            "Privileged request without an MFA session allowed for user {UserId} (Identity:TwoFactor:EnforcementMode=Warn)",
            userId);

        if (context.Resource is HttpContext http && !http.Response.HasStarted)
        {
            http.Response.Headers[AuthorizationErrorCodes.HeaderName] = AuthorizationErrorCodes.MfaRequired;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    public static bool RequiresMfa(ClaimsPrincipal user, IEnumerable<string> requiredForRoles) =>
        requiredForRoles.Any(r => user.IsInRole(r) ||
                                  (ApiKeyPrincipal.IsApiKey(user) && ApiKeyPrincipal.OwnerRoles(user).Contains(r)));
}
