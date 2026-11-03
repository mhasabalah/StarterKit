using System.Diagnostics;
using System.Reflection;
using Carter;
using Identity.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    // ── Real app container + endpoints, without a database ───────────────────────────────────────────────

    private static readonly Lazy<IServiceProvider> Services = new(BuildServices);

    private static IReadOnlyList<RouteEndpoint> LoadEndpoints()
    {
        var builder = new CapturingEndpointRouteBuilder(Services.Value);
        builder.MapCarter();

        return builder.DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();
    }

    /// <summary>Runs Program's top-level statements until the host is built, then stops them (no DB, no server).</summary>
    private static IServiceProvider BuildServices()
    {
        var captured = new TaskCompletionSource<IServiceProvider>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = DiagnosticListener.AllListeners.Subscribe(new HostCapture(captured));

        var entryPoint = typeof(Program).Assembly.EntryPoint!;
        string[] args =
        [
            "--environment=Testing",
            "--ConnectionStrings:starterkit=Host=localhost;Database=never-opened;Username=x;Password=x",
            "--Redis:Enabled=false",
            "--JwtSettings:SecretKey=architecture-tests-signing-key-0123456789",
            "--JwtSettings:Issuer=tests",
            "--JwtSettings:Audience=tests"
        ];

        var thread = new Thread(() =>
        {
            try
            {
                entryPoint.Invoke(null, [args]);
                captured.TrySetException(new InvalidOperationException("Program finished without building a host."));
            }
            catch (Exception ex) when (Unwrap(ex) is StopTheHostException)
            {
                // Expected: stopped right after Build().
            }
            catch (Exception ex)
            {
                captured.TrySetException(Unwrap(ex));
            }
        });
        thread.Start();

        if (!captured.Task.Wait(TimeSpan.FromMinutes(2)))
        {
            throw new TimeoutException("Program did not build its host within 2 minutes.");
        }

        return captured.Task.Result;
    }

    private static Exception Unwrap(Exception ex) =>
        ex is TargetInvocationException { InnerException: { } inner } ? Unwrap(inner)
        : ex is AggregateException { InnerException: { } aggregateInner } ? Unwrap(aggregateInner)
        : ex;

    private sealed class StopTheHostException : Exception;

    private sealed class HostCapture(TaskCompletionSource<IServiceProvider> captured)
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.Extensions.Hosting")
            {
                listener.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == "HostBuilt" && value.Value is IHost host && !captured.Task.IsCompleted)
            {
                captured.TrySetResult(host.Services);
                throw new StopTheHostException();
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class CapturingEndpointRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
