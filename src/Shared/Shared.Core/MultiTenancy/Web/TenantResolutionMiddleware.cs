using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Shared.Core.Exceptions;

namespace Shared.Core.MultiTenancy.Web;

/// <summary>
/// Resolves the tenant of an HTTP request and runs the rest of the pipeline inside its <see cref="TenantScope"/>.
/// Runs AFTER authentication (needs the user) and BEFORE authorization (tenant permissions need the membership).
/// <para><b>Sources</b> (multi-tenant mode):</para>
/// <list type="number">
/// <item><b>JWT claim</b> <c>tid</c> - authoritative for authenticated users. A header/host naming ANOTHER tenant is
/// rejected (403 <c>tenant_mismatch</c>) instead of being silently ignored.</item>
/// <item><b>Host</b> - <c>{identifier}.{RootDomain}</c> or a verified custom domain.</item>
/// <item><b>Header</b> <c>X-Tenant</c> - tenant id or identifier.</item>
/// </list>
/// <para><b>Checks</b>: tenant exists (404), is serviceable (403 <c>tenant_suspended</c>/<c>tenant_unavailable</c>),
/// a token's tenant still has the user as a member (403) with an unchanged membership stamp (401 → refresh).
/// Unauthenticated callers (and authenticated non-members arriving via host/header) get the tenant WITHOUT a
/// membership: they can reach only endpoints that need no tenant permission (e.g. public storefront pages).</para>
/// <para>Single-tenant mode: always the Default tenant; claims/headers/hosts are ignored.</para>
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<MultiTenancyOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    private static readonly HashSet<string> SafeMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    private static readonly ConcurrentDictionary<Endpoint, TenantRequirement> Requirements = new();

    public async Task InvokeAsync(HttpContext context, ITenantStore store, ITenantMembershipResolver memberships,
        TenantDataContextRegistry registry)
    {
        var endpoint = context.GetEndpoint();
        var requirement = endpoint is null
            ? TenantRequirement.Optional
            : Requirements.GetOrAdd(endpoint, e => RequirementOf(e, registry));

        if (requirement == TenantRequirement.Host)
        {
            // Control-plane endpoint: explicitly tenant-less (tenant data access fails closed).
            using (TenantScope.BeginEmpty())
            {
                await next(context);
            }

            return;
        }

        Resolution? resolution;
        try
        {
            resolution = await ResolveAsync(context, store, memberships, options.Value);
        }
        catch (CustomException ex) when (requirement == TenantRequirement.Optional)
        {
            // Endpoints that do not need a tenant (profile, logout, public pages) keep working with an invalid tenant
            // context (revoked membership, suspended tenant, unknown header...): they just run tenant-less.
            logger.LogDebug("Tenant ignored for optional endpoint {Path}: {Reason}", context.Request.Path, ex.Message);
            resolution = null;
        }

        if (resolution is null)
        {
            if (requirement == TenantRequirement.Required)
            {
                throw new TenantNotResolvedException();
            }

            using (TenantScope.BeginEmpty())
            {
                await next(context);
            }

            return;
        }

        var (tenant, membership, branches) = resolution;
        context.Items[typeof(TenantInfo)] = tenant;
        Activity.Current?.SetTag("tenant.id", tenant.Id.ToString());
        Activity.Current?.SetTag("tenant.identifier", tenant.Identifier);

        using (TenantScope.Begin(tenant, branches, membership))
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenant.Id,
            ["TenantIdentifier"] = tenant.Identifier
        }))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Explicit metadata wins; otherwise endpoints whose handler lives in an assembly that owns a tenant-data context
    /// require a tenant (they exist to work on tenant data), everything else is optional.
    /// </summary>
    private static TenantRequirement RequirementOf(Endpoint endpoint, TenantDataContextRegistry registry)
    {
        if (endpoint.Metadata.GetMetadata<TenantRequirementMetadata>() is { } explicitRequirement)
        {
            return explicitRequirement.Requirement;
        }

        var handlerAssembly = endpoint.Metadata.GetMetadata<MethodInfo>()?.DeclaringType?.Assembly;
        return handlerAssembly is not null && registry.ContextTypes.Any(t => t.Assembly == handlerAssembly)
            ? TenantRequirement.Required
            : TenantRequirement.Optional;
    }

    private sealed record Resolution(TenantInfo Tenant, TenantMembershipInfo? Membership, BranchScope Branches);

    private async Task<Resolution?> ResolveAsync(HttpContext context, ITenantStore store,
        ITenantMembershipResolver memberships, MultiTenancyOptions opts)
    {
        var ct = context.RequestAborted;
        var userId = GetUserId(context.User);

        var (tenant, fromClaim) = opts.Enabled
            ? await ResolveTenantAsync(context, store, opts, ct)
            : (TenantDefaults.DefaultTenant, false);

        if (tenant is null)
            return null;

        EnsureServiceable(tenant, context.Request.Method, opts);

        TenantMembershipInfo? membership = null;
        if (userId is { } uid)
        {
            membership = await memberships.GetMembershipAsync(uid, tenant.Id, ct);

            if (opts.Enabled && fromClaim)
            {
                if (membership is null)
                {
                    throw new TenantAccessDeniedException(TenantErrors.NotAMember,
                        "you are no longer a member of this tenant.");
                }

                var stamp = context.User.FindFirstValue(TenantClaimTypes.MembershipStamp);
                if (stamp is not null && !string.Equals(stamp, membership.Stamp, StringComparison.Ordinal))
                {
                    throw new StaleTenantMembershipException();
                }
            }
        }

        return new Resolution(tenant, membership, ResolveBranchScope(context, membership, opts));
    }

    private async Task<(TenantInfo? Tenant, bool FromClaim)> ResolveTenantAsync(
        HttpContext context, ITenantStore store, MultiTenancyOptions opts, CancellationToken ct)
    {
        var resolution = opts.Resolution;

        TenantInfo? fromClaim = null;
        if (context.User.Identity?.IsAuthenticated == true &&
            context.User.FindFirstValue(resolution.ClaimType) is { Length: > 0 } claimValue)
        {
            if (!Guid.TryParse(claimValue, out var claimTenantId))
            {
                throw new UnauthorizedException("Invalid tenant claim.");
            }

            fromClaim = await store.FindByIdAsync(claimTenantId, ct)
                        ?? throw new NotFoundException($"{TenantErrors.TenantNotFound}: the tenant of your session no longer exists.");
        }

        var fromHost = resolution.EnableHost ? await FromHostAsync(context, store, resolution, ct) : null;
        var fromHeader = resolution.EnableHeader ? await FromHeaderAsync(context, store, resolution, ct) : null;

        if (fromClaim is not null)
        {
            if ((fromHost is not null && fromHost.Id != fromClaim.Id) ||
                (fromHeader is not null && fromHeader.Id != fromClaim.Id))
            {
                logger.LogWarning("Tenant mismatch: token tenant {ClaimTenant}, host {HostTenant}, header {HeaderTenant}",
                    fromClaim.Id, fromHost?.Id, fromHeader?.Id);
                throw new TenantAccessDeniedException(TenantErrors.TenantMismatch,
                    "the requested tenant differs from the tenant of your session; switch tenant first.");
            }

            return (fromClaim, true);
        }

        if (fromHost is not null && fromHeader is not null && fromHost.Id != fromHeader.Id)
        {
            throw new TenantAccessDeniedException(TenantErrors.TenantMismatch,
                "the tenant header differs from the tenant of the host.");
        }

        return (fromHost ?? fromHeader, false);
    }

    private static async Task<TenantInfo?> FromHostAsync(HttpContext context, ITenantStore store,
        TenantResolutionOptions resolution, CancellationToken ct)
    {
        var host = context.Request.Host.Host.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || host == "localhost" || IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var root = resolution.RootDomain?.Trim().TrimEnd('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(root))
        {
            if (host == root)
                return null;

            if (host.EndsWith("." + root, StringComparison.Ordinal))
            {
                var label = host[..^(root.Length + 1)];
                if (label.Contains('.') ||
                    resolution.ReservedSubdomains.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    return null;
                }

                return await store.FindByIdentifierAsync(label, ct)
                       ?? throw new NotFoundException($"{TenantErrors.TenantNotFound}: unknown tenant '{label}'.");
            }
        }

        // Any other host may be a verified custom domain (unknown hosts are simply "no tenant").
        return await store.FindByHostAsync(host, ct);
    }

    private static async Task<TenantInfo?> FromHeaderAsync(HttpContext context, ITenantStore store,
        TenantResolutionOptions resolution, CancellationToken ct)
    {
        var value = context.Request.Headers[resolution.HeaderName].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        if (value.Length > 100)
        {
            throw new BadRequestException($"{TenantErrors.TenantNotFound}: invalid tenant header.");
        }

        var tenant = Guid.TryParse(value, out var id)
            ? await store.FindByIdAsync(id, ct)
            : await store.FindByIdentifierAsync(value, ct);

        return tenant ?? throw new NotFoundException($"{TenantErrors.TenantNotFound}: unknown tenant '{value}'.");
    }

    private static void EnsureServiceable(TenantInfo tenant, string method, MultiTenancyOptions opts)
    {
        switch (tenant.Status)
        {
            case TenantStatus.Active:
                return;
            case TenantStatus.Suspended when opts.SuspendedTenantMode == SuspendedTenantMode.ReadOnly &&
                                             SafeMethods.Contains(method):
                return;
            case TenantStatus.Migrating when SafeMethods.Contains(method):
                return;
            case TenantStatus.Migrating:
                throw new TenantMigratingException();
            case TenantStatus.Suspended:
                throw new TenantAccessDeniedException(TenantErrors.TenantSuspended,
                    opts.SuspendedTenantMode == SuspendedTenantMode.ReadOnly
                        ? "this tenant is suspended and read-only."
                        : "this tenant is suspended.");
            case TenantStatus.Deleted:
                throw new NotFoundException($"{TenantErrors.TenantNotFound}: this tenant no longer exists.");
            default:
                throw new TenantAccessDeniedException(TenantErrors.TenantUnavailable,
                    $"this tenant is not available ({tenant.Status}).");
        }
    }

    private static BranchScope ResolveBranchScope(HttpContext context, TenantMembershipInfo? membership,
        MultiTenancyOptions opts)
    {
        var scope = membership?.Branches ?? BranchScope.Unrestricted;

        var header = context.Request.Headers[opts.Resolution.BranchHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header))
            return scope;

        if (!Guid.TryParse(header, out var branchId) || !scope.Allows(branchId))
        {
            throw new TenantAccessDeniedException(TenantErrors.BranchNotAllowed,
                "the selected branch is outside your branch scope.");
        }

        return scope with { ActiveBranchId = branchId };
    }

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id)
            ? id
            : null;
}

public static class TenantResolutionApplicationBuilderExtensions
{
    /// <summary>
    /// Impersonation guard + tenant resolution. Place between <c>UseAuthentication()</c> and <c>UseAuthorization()</c>.
    /// </summary>
    public static IApplicationBuilder UseMultiTenancy(this IApplicationBuilder app) =>
        app.UseMiddleware<ImpersonationGuardMiddleware>()
            .UseMiddleware<TenantResolutionMiddleware>();
}
