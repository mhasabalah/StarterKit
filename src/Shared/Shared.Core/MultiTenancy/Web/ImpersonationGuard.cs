using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Shared.Core.MultiTenancy.Web;

/// <summary>Endpoint metadata: impersonated sessions (token with an <c>act</c> claim) may not call this endpoint.</summary>
public sealed class DisallowImpersonationMetadata;

/// <summary>
/// Which code impersonated sessions may only READ (<c>MultiTenancy</c> impersonation guard). Modules add their
/// assembly (Identity: credentials, 2FA, sessions, profile - an impersonator must never change them).
/// </summary>
public sealed class ImpersonationGuardOptions
{
    public HashSet<Assembly> ReadOnlyAssemblies { get; } = [];
}

public static class ImpersonationEndpointExtensions
{
    /// <summary>
    /// Impersonated sessions get 403 <c>impersonation_restricted</c> here (destructive or ownership actions:
    /// tenant deletion, ownership transfer, data export…).
    /// </summary>
    public static TBuilder DisallowImpersonation<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint => endpoint.Metadata.Add(new DisallowImpersonationMetadata()));
        return builder;
    }
}

/// <summary>
/// Restricts impersonation tokens (support staff acting as a tenant member; claim <c>act</c> = the platform user):
/// <list type="bullet">
/// <item>endpoints marked <see cref="ImpersonationEndpointExtensions.DisallowImpersonation{TBuilder}"/> → 403;</item>
/// <item>state-changing calls to <c>HostOnly()</c> endpoints (switch tenant, create tenant, accept invitation…) → 403,
/// so an impersonation can never mint a regular session or act outside its tenant;</item>
/// <item>state-changing calls to endpoints of <see cref="ImpersonationGuardOptions.ReadOnlyAssemblies"/> → 403.</item>
/// </list>
/// Every log line of an impersonated request carries <c>ImpersonatorId</c>; tenant audit entries record it too.
/// </summary>
public sealed class ImpersonationGuardMiddleware(
    RequestDelegate next,
    IOptions<ImpersonationGuardOptions> options,
    ILogger<ImpersonationGuardMiddleware> logger)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    public async Task InvokeAsync(HttpContext context)
    {
        var actor = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(TenantClaimTypes.Actor)
            : null;

        if (string.IsNullOrEmpty(actor))
        {
            await next(context);
            return;
        }

        if (IsRestricted(context.GetEndpoint(), context.Request.Method, options.Value))
        {
            logger.LogWarning("Impersonation by {ImpersonatorId} blocked on {Method} {Path}", actor,
                context.Request.Method, context.Request.Path);
            throw new TenantAccessDeniedException(TenantErrors.ImpersonationRestricted,
                "this action is not available while impersonating a user.");
        }

        Activity.Current?.SetTag("impersonator.id", actor);
        using (logger.BeginScope(new Dictionary<string, object> { ["ImpersonatorId"] = actor }))
        {
            await next(context);
        }
    }

    internal static bool IsRestricted(Endpoint? endpoint, string method, ImpersonationGuardOptions options)
    {
        if (endpoint is null)
            return false;
        if (endpoint.Metadata.GetMetadata<DisallowImpersonationMetadata>() is not null)
            return true;
        if (SafeMethods.Contains(method))
            return false;

        if (endpoint.Metadata.GetMetadata<TenantRequirementMetadata>()?.Requirement == TenantRequirement.Host)
            return true;

        var assembly = endpoint.Metadata.GetMetadata<MethodInfo>()?.DeclaringType?.Assembly;
        return assembly is not null && options.ReadOnlyAssemblies.Contains(assembly);
    }
}
