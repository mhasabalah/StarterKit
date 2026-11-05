using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Shared.Core.Exceptions;

namespace Shared.Core.Authorization;

/// <summary>Claims of a principal authenticated by an API key (scheme issued by the Identity module).</summary>
public static class ApiKeyClaimTypes
{
    /// <summary>Id of the key. Its presence is what marks the principal as "API key" everywhere.</summary>
    public const string KeyId = "api_key_id";

    /// <summary>One claim per scope (permission name) the key is limited to.</summary>
    public const string Scope = "api_key_scope";

    /// <summary>
    /// The roles of the key's OWNER, one claim per role. Deliberately NOT <c>ClaimTypes.Role</c>: an API key principal
    /// carries no role (so <c>IsInRole</c>, role policies and "is admin" checks never match) and no <c>is_admin</c>
    /// claim; only <see cref="PermissionClaimsTransformation"/> (and the MFA gate) read these to derive permissions,
    /// which are then intersected with the key's scopes.
    /// </summary>
    public const string OwnerRole = "api_key_owner_role";

    /// <summary>Authentication scheme names.</summary>
    public const string SchemeName = "ApiKey";

    /// <summary>Policy scheme that picks Bearer or ApiKey per request.</summary>
    public const string PolicySchemeName = "BearerOrApiKey";

    /// <summary>Error code of a 403 caused by <see cref="ApiKeyEndpointExtensions.DisallowApiKey{TBuilder}"/>.</summary>
    public const string RestrictedErrorCode = "api_key_restricted";
}

public static class ApiKeyPrincipal
{
    public static bool IsApiKey(ClaimsPrincipal? principal) =>
        principal?.Identities.Any(i => i.IsAuthenticated && i.HasClaim(c => c.Type == ApiKeyClaimTypes.KeyId)) == true;

    public static IEnumerable<string> OwnerRoles(ClaimsPrincipal principal) =>
        principal.FindAll(ApiKeyClaimTypes.OwnerRole).Select(c => c.Value).Distinct(StringComparer.Ordinal);

    public static HashSet<string> Scopes(ClaimsPrincipal principal) =>
        principal.FindAll(ApiKeyClaimTypes.Scope).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A key may use a permission only when it was granted as a scope. Principals that are not API keys are never
    /// restricted by this check. (Effective permissions = role/membership permissions AND key scopes.)
    /// </summary>
    public static bool Allows(ClaimsPrincipal principal, string permission) =>
        !IsApiKey(principal) || Scopes(principal).Contains(permission);
}

/// <summary>Endpoint metadata: requests authenticated with an API key may not call this endpoint.</summary>
public sealed class DisallowApiKeyMetadata;

public static class ApiKeyEndpointExtensions
{
    /// <summary>
    /// API keys get 403 <c>api_key_restricted</c> here: credential, 2FA, session, impersonation and API-key management
    /// endpoints must be driven by an interactive session, never by a long-lived machine credential.
    /// </summary>
    public static TBuilder DisallowApiKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(new DisallowApiKeyMetadata()));
        return builder;
    }
}

/// <summary>Enforces <see cref="ApiKeyEndpointExtensions.DisallowApiKey{TBuilder}"/> (runs after authentication).</summary>
public sealed class ApiKeyRestrictionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (ApiKeyPrincipal.IsApiKey(context.User) &&
            context.GetEndpoint()?.Metadata.GetMetadata<DisallowApiKeyMetadata>() is not null)
        {
            throw new ForbiddenException("This endpoint cannot be called with an API key.")
            {
                ErrorCode = ApiKeyClaimTypes.RestrictedErrorCode
            };
        }

        await next(context);
    }
}
