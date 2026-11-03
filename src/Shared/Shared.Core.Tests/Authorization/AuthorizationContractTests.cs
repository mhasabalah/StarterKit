using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Shared.Core.Authorization;

namespace Shared.Core.Tests.Authorization;

/// <summary>The cross-module authorization contract (permission names, policy names, the amr/MFA claim).</summary>
public sealed class AuthorizationContractTests
{
    private sealed class CapturingConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = new();

        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }

    [Fact]
    public void Every_permission_constant_is_listed_in_All_exactly_once()
    {
        var constants = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name is not nameof(Permissions.ClaimType) and not nameof(Permissions.PolicyPrefix))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Permissions.All.Should().BeEquivalentTo(constants).And.OnlyHaveUniqueItems();
        Permissions.All.Should().OnlyContain(p => p == p.ToLowerInvariant() && !p.Contains(' '));
    }

    [Fact]
    public void Policy_name_is_the_prefixed_permission()
    {
        Permissions.PolicyName(Permissions.BillingRead).Should().Be("perm:billing.read");
    }

    [Fact]
    public void RequirePermission_adds_the_named_permission_policy()
    {
        var builder = new CapturingConventionBuilder();
        builder.RequirePermission(Permissions.AuditRead);

        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/x"), 0);
        builder.Conventions.ForEach(c => c(endpoint));

        endpoint.Metadata.OfType<IAuthorizeData>().Select(a => a.Policy).Should().Equal("perm:audit.read");
    }

    [Theory]
    [InlineData(AuthenticationMethods.ClaimType, "mfa", true)]
    [InlineData(AuthenticationMethods.MappedClaimType, "mfa", true)]
    [InlineData(AuthenticationMethods.ClaimType, "pwd", false)]
    [InlineData("mfa", "true", false)]
    public void IsMultiFactor_reads_the_amr_claim_mapped_or_not(string type, string value, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(type, value)], "test"));

        AuthenticationMethods.IsMultiFactor(principal).Should().Be(expected);
        AuthenticationMethods.IsMultiFactor(principal.Claims).Should().Be(expected);
    }

    [Fact]
    public void IsMultiFactor_is_null_safe()
    {
        AuthenticationMethods.IsMultiFactor((ClaimsPrincipal?)null).Should().BeFalse();
        AuthenticationMethods.IsMultiFactor((IEnumerable<Claim>?)null).Should().BeFalse();
    }
}
