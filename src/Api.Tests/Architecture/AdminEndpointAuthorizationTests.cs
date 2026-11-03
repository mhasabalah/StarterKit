using Carter;
using Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;

namespace Api.Tests.Architecture;

/// <summary>
/// Every endpoint under an <c>/admin</c> (or <c>/superadmin</c>) route must require an admin-grade policy:
/// AdminOnly / SuperAdminOnly, or a <c>perm:*</c> permission policy - never just "authenticated" and never anonymous.
/// <para>
/// Uses the REAL app: Program runs up to <c>builder.Build()</c> (no database, no server start - the same trick EF
/// tooling and WebApplicationFactory use), then every Carter module of the real DI container maps its routes and
/// the effective policy of each endpoint is computed with the real <see cref="IAuthorizationPolicyProvider"/>.
/// </para>
/// </summary>
public sealed class AdminEndpointAuthorizationTests
{
    private static readonly Lazy<IReadOnlyList<RouteEndpoint>> Endpoints = new(LoadEndpoints);

    private static readonly string[] AdminSegments = ["admin", "superadmin"];

    [Fact]
    public void The_app_exposes_admin_endpoints_to_check()
    {
        Endpoints.Value.Should().Contain(e => IsAdminRoute(e), "the scan must actually find the admin routes");
        Endpoints.Value.Should().Contain(e => e.RoutePattern.RawText!.Contains("admin/identity/audit"));
    }

    [Fact]
    public async Task Every_admin_route_requires_an_admin_or_permission_policy()
    {
        var policyProvider = Services.Value.GetRequiredService<IAuthorizationPolicyProvider>();
        var violations = new List<string>();

        foreach (var endpoint in Endpoints.Value.Where(IsAdminRoute))
        {
            var name = $"{string.Join(",", endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Routing.IHttpMethodMetadata>()
                .SelectMany(m => m.HttpMethods))} {endpoint.RoutePattern.RawText}";

            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                violations.Add($"{name}: allows anonymous access");
                continue;
            }

            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var namedPolicies = authorizeData.Select(a => a.Policy).Where(p => p is not null).ToList();
            var policy = await AuthorizationPolicy.CombineAsync(
                policyProvider, authorizeData, endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());

            if (!IsAdminGrade(namedPolicies!, policy))
            {
                violations.Add($"{name}: policies [{string.Join(", ", namedPolicies)}] are not admin-grade");
            }
        }

        violations.Should().BeEmpty(
            "admin routes must use .RequirePermission(Permissions.X) (preferred) or AdminOnly/SuperAdminOnly");
    }

    /// <summary>
    /// Authorization is explicit: every endpoint declares it (<c>RequireAuthorization</c> / <c>RequirePermission</c> /
    /// <c>RequireTenantPermission</c> / ... or an explicit <c>AllowAnonymous</c>). The fallback policy (authenticated
    /// user) is only a safety net - relying on it hides intent and makes public endpoints impossible to audit.
    /// </summary>
    [Fact]
    public void Every_endpoint_declares_authorization_explicitly()
    {
        var violations = Endpoints.Value
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                        !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any() &&
                        !e.Metadata.GetOrderedMetadata<AuthorizationPolicy>().Any())
            .Select(DisplayName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, violations).Should().BeEmpty(
            "every endpoint must call .RequireAuthorization(...)/.RequirePermission(...)/.RequireTenantPermission(...) " +
            "or an explicit .AllowAnonymous() - see CLAUDE.md > Auth is explicit");
    }

    /// <summary>The runtime safety net: an endpoint that declares nothing still requires an authenticated user.</summary>
    [Fact]
    public void Carter_group_requires_authorization_by_default_but_keeps_explicit_choices()
    {
        var builder = new CapturingEndpointRouteBuilder(Services.Value);
        var group = builder.MapGroup(string.Empty).RequireAuthorizationByDefault();
        group.MapGet("/undeclared", () => "x");
        group.MapGet("/public", () => "x").AllowAnonymous();
        group.MapGet("/permission", () => "x").RequirePermission(Permissions.UsersRead);

        var endpoints = builder.DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .ToDictionary(e => e.RoutePattern.RawText!.TrimStart('/'));

        endpoints["undeclared"].Metadata.GetOrderedMetadata<IAuthorizeData>().Should().ContainSingle();
        endpoints["public"].Metadata.GetOrderedMetadata<IAuthorizeData>().Should().BeEmpty();
        endpoints["public"].Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull();
        endpoints["permission"].Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().ContainSingle(a => a.Policy == Permissions.PolicyName(Permissions.UsersRead));
    }

    private static string DisplayName(RouteEndpoint endpoint) =>
        $"{string.Join(",", endpoint.Metadata.GetOrderedMetadata<IHttpMethodMetadata>().SelectMany(m => m.HttpMethods))} " +
        endpoint.RoutePattern.RawText;

    [Fact]
    public async Task Every_permission_policy_used_by_an_endpoint_is_registered()
    {
        var policyProvider = Services.Value.GetRequiredService<IAuthorizationPolicyProvider>();

        var used = Endpoints.Value
            .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Select(a => a.Policy)
            .Where(p => p is not null && p.StartsWith(Permissions.PolicyPrefix))
            .Distinct()
            .ToList();

        used.Should().NotBeEmpty();
        foreach (var policy in used)
        {
            (await policyProvider.GetPolicyAsync(policy!)).Should().NotBeNull($"'{policy}' must be a registered policy");
        }
    }

    private static bool IsAdminRoute(RouteEndpoint endpoint) =>
        endpoint.RoutePattern.PathSegments.Any(segment => segment.Parts.Any(part =>
            part is Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal &&
            AdminSegments.Contains(literal.Content, StringComparer.OrdinalIgnoreCase)));

    /// <summary>
    /// Named AdminOnly / SuperAdminOnly / perm:* policy, or (for inline policies) an effective policy that demands a
    /// permission or a role restricted to Admin/SuperAdmin.
    /// </summary>
    private static bool IsAdminGrade(IReadOnlyCollection<string> namedPolicies, AuthorizationPolicy? policy)
    {
        if (namedPolicies.Any(p => p is PolicyConstants.AdminOnly or PolicyConstants.SuperAdminOnly ||
                                   p.StartsWith(Permissions.PolicyPrefix, StringComparison.Ordinal)))
        {
            return true;
        }

        if (policy is null)
        {
            return false;
        }

        return policy.Requirements.Any(r =>
            r is PermissionRequirement ||
            (r is RolesAuthorizationRequirement roles && roles.AllowedRoles.Any() &&
             roles.AllowedRoles.All(role => role is RoleConstants.Admin or RoleConstants.SuperAdmin)));
    }

    // ── Real app container + endpoints, without a database (see RealHost) ─────────────────────────────

    private static readonly Lazy<IServiceProvider> Services = new(() => RealHost.Services);

    private static IReadOnlyList<RouteEndpoint> LoadEndpoints()
    {
        var builder = new CapturingEndpointRouteBuilder(Services.Value);
        builder.MapCarter();

        return builder.DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
    }

    private sealed class CapturingEndpointRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
