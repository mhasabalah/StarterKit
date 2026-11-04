using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Shared.Core.Authorization;

/// <summary>
/// Deny-by-default for application endpoints: any endpoint of the group that declares NO authorization metadata
/// (no <c>RequireAuthorization</c>/<c>RequirePermission</c>/<c>RequireTenantPermission</c>/... and no
/// <c>AllowAnonymous</c>) gets <c>RequireAuthorization()</c> (authenticated user).
/// <para>
/// Why a group convention and not <c>AuthorizationOptions.FallbackPolicy</c>: the fallback policy also applies to
/// requests WITHOUT an endpoint (middleware such as the Hangfire dashboard with its own Basic auth, Swagger UI static
/// files) and to framework endpoints (health, OpenAPI, Scalar) - it would break them. This convention only touches the
/// application's Carter endpoints. It is a safety net: the architecture test
/// <c>AdminEndpointAuthorizationTests.Every_endpoint_declares_authorization_explicitly</c> still requires every endpoint
/// to state its intent explicitly.
/// </para>
/// </summary>
public static class AuthorizationByDefaultExtensions
{
    public static TBuilder RequireAuthorizationByDefault<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        // Finally conventions run after every endpoint-level convention, so explicit choices are already visible.
        builder.Finally(endpoint =>
        {
            if (!DeclaresAuthorization(endpoint.Metadata))
            {
                endpoint.Metadata.Add(new AuthorizeAttribute());
            }
        });

        return builder;
    }

    /// <summary>True when the metadata states an authorization intent (a requirement or an explicit anonymous opt-out).</summary>
    public static bool DeclaresAuthorization(IEnumerable<object> metadata) =>
        metadata.Any(m => m is IAuthorizeData or IAllowAnonymous or AuthorizationPolicy);
}
