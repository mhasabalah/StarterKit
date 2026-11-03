using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Core.Authorization;

/// <summary>
/// Expands the caller's ROLE claims into <see cref="Permissions.ClaimType"/> claims (via <see cref="RolePermissionMap"/>)
/// right after authentication, for every scheme. Everything that checks permissions (the <c>perm:*</c> policies,
/// <c>IHttpContextService.HasClaim</c>, the single-tenant fallback of tenant permissions) then works the same no matter
/// which identity provider issued the token - tokens never need to carry permissions.
/// <list type="bullet">
/// <item>Unauthenticated principals are returned unchanged.</item>
/// <item>Idempotent: <c>AuthenticateAsync</c> may run several times per request; the expansion is added once, in its own
/// identity (<see cref="AuthenticationType"/>), on a CLONE of the principal (the cached ticket is never mutated).</item>
/// <item>Permission claims already present (a provider that issues them, test headers) are kept; only missing ones are added.</item>
/// <item>Roles are read with each identity's own <see cref="ClaimsIdentity.RoleClaimType"/> - the same claims
/// <c>IsInRole</c> and role policies use.</item>
/// </list>
/// </summary>
public sealed class PermissionClaimsTransformation : IClaimsTransformation
{
    /// <summary>Authentication type of the identity holding the derived permission claims (also the idempotency marker).</summary>
    public const string AuthenticationType = "role-permissions";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var authenticated = principal.Identities
            .Where(i => i.IsAuthenticated && i.AuthenticationType != AuthenticationType)
            .ToList();

        if (authenticated.Count == 0 ||
            principal.Identities.Any(i => i.AuthenticationType == AuthenticationType))
        {
            return Task.FromResult(principal);
        }

        var roles = authenticated.SelectMany(i => i.FindAll(i.RoleClaimType)).Select(c => c.Value);
        var present = principal.FindAll(Permissions.ClaimType).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var missing = RolePermissionMap.For(roles).Where(p => !present.Contains(p));

        var transformed = principal.Clone();
        transformed.AddIdentity(new ClaimsIdentity(
            missing.Select(p => new Claim(Permissions.ClaimType, p)),
            AuthenticationType));

        return Task.FromResult(transformed);
    }
}

public static class PermissionClaimsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PermissionClaimsTransformation"/> as THE claims transformation (replaces the framework no-op).
    /// Any identity-provider module must call this (the Identity module does, in <c>AddIdentityAuthorizationPolicies</c>).
    /// </summary>
    public static IServiceCollection AddRolePermissionClaims(this IServiceCollection services)
    {
        services.RemoveAll<IClaimsTransformation>();
        services.AddSingleton<IClaimsTransformation, PermissionClaimsTransformation>();
        return services;
    }
}
