using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Authorization;

namespace Shared.Core.Tests.Authorization;

/// <summary>Roles in the token -> permission claims on the principal, for any identity provider.</summary>
public sealed class PermissionClaimsTransformationTests
{
    private readonly PermissionClaimsTransformation _sut = new();

    private static ClaimsPrincipal Authenticated(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Prepend(new Claim(ClaimTypes.NameIdentifier, "u1")),
            authenticationType: "Bearer"));

    private static List<string> PermissionsOf(ClaimsPrincipal principal) =>
        principal.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList();

    [Fact]
    public async Task Anonymous_principal_is_returned_unchanged()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, RoleConstants.SuperAdmin)]));

        var result = await _sut.TransformAsync(anonymous);

        result.Should().BeSameAs(anonymous);
        PermissionsOf(result).Should().BeEmpty("an unauthenticated identity must never gain permissions");
    }

    [Fact]
    public async Task Single_role_gets_its_permissions()
    {
        var result = await _sut.TransformAsync(Authenticated(RoleConstants.Support));

        PermissionsOf(result).Should().BeEquivalentTo(RolePermissionMap.For(RoleConstants.Support));
    }

    [Fact]
    public async Task Multiple_roles_get_the_deduplicated_union()
    {
        var result = await _sut.TransformAsync(Authenticated(RoleConstants.Finance, RoleConstants.Support, RoleConstants.Support));

        var permissions = PermissionsOf(result);
        permissions.Should().OnlyHaveUniqueItems();
        permissions.Should().BeEquivalentTo(RolePermissionMap.For([RoleConstants.Finance, RoleConstants.Support]));
    }

    [Fact]
    public async Task SuperAdmin_gets_every_platform_and_tenant_permission()
    {
        var result = await _sut.TransformAsync(Authenticated(RoleConstants.SuperAdmin));

        PermissionsOf(result).Should().BeEquivalentTo(Permissions.All.Concat(TenantPermissions.All));
    }

    [Theory]
    [InlineData(RoleConstants.User)]
    [InlineData("NotARole")]
    public async Task Roles_without_permissions_get_none(string role)
    {
        var result = await _sut.TransformAsync(Authenticated(role));

        PermissionsOf(result).Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_permission_claims_are_kept_and_not_duplicated()
    {
        var principal = Authenticated(RoleConstants.Support);
        ((ClaimsIdentity)principal.Identity!).AddClaims(
        [
            new Claim(Permissions.ClaimType, Permissions.BillingRead),    // also granted by Support
            new Claim(Permissions.ClaimType, Permissions.CatalogModerate) // granted explicitly only
        ]);

        var result = await _sut.TransformAsync(principal);

        var permissions = PermissionsOf(result);
        permissions.Should().OnlyHaveUniqueItems();
        permissions.Should().BeEquivalentTo(RolePermissionMap.For(RoleConstants.Support).Append(Permissions.CatalogModerate));
    }

    [Fact]
    public async Task Is_idempotent()
    {
        var once = await _sut.TransformAsync(Authenticated(RoleConstants.Admin));
        var twice = await _sut.TransformAsync(once);

        twice.Should().BeSameAs(once);
        PermissionsOf(twice).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Does_not_mutate_the_incoming_principal()
    {
        var principal = Authenticated(RoleConstants.Admin);

        var result = await _sut.TransformAsync(principal);

        result.Should().NotBeSameAs(principal);
        PermissionsOf(principal).Should().BeEmpty();
        principal.Identities.Should().ContainSingle();
    }

    [Fact]
    public async Task Uses_the_identity_role_claim_type_of_each_provider()
    {
        // e.g. a Keycloak bearer configured with RoleClaimType = "roles"
        var identity = new ClaimsIdentity([new Claim("roles", RoleConstants.Support)], "Bearer", "name", "roles");

        var result = await _sut.TransformAsync(new ClaimsPrincipal(identity));

        PermissionsOf(result).Should().BeEquivalentTo(RolePermissionMap.For(RoleConstants.Support));
    }

    [Fact]
    public void Registration_replaces_the_framework_no_op_transformation()
    {
        var services = new ServiceCollection();
        services.AddAuthentication();
        services.AddRolePermissionClaims();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IClaimsTransformation>().Should().ContainSingle()
            .Which.Should().BeOfType<PermissionClaimsTransformation>();
    }
}
