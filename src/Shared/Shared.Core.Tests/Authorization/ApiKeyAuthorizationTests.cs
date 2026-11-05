using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Shared.Core.Authorization;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Tests.MultiTenancy;

namespace Shared.Core.Tests.Authorization;

/// <summary>API keys: effective permissions = role/membership permissions INTERSECT key scopes; restricted endpoints.</summary>
public sealed class ApiKeyAuthorizationTests
{
    private static ClaimsPrincipal ApiKeyPrincipal(string[] roles, string[] scopes) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ApiKeyClaimTypes.OwnerRole, r))
                .Concat(scopes.Select(s => new Claim(ApiKeyClaimTypes.Scope, s)))
                .Append(new Claim(ClaimTypes.NameIdentifier, "u1"))
                .Append(new Claim(ApiKeyClaimTypes.KeyId, Guid.NewGuid().ToString())),
            authenticationType: "ApiKey"));

    private static List<string> PermissionsOf(ClaimsPrincipal principal) =>
        principal.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList();

    [Fact]
    public async Task Api_key_gets_only_the_role_permissions_that_are_also_scopes()
    {
        var principal = ApiKeyPrincipal([RoleConstants.Support], [Permissions.BillingRead, Permissions.UsersManage]);

        var result = await new PermissionClaimsTransformation().TransformAsync(principal);

        // UsersManage is a scope but Support does not hold it; UsersRead is held but not a scope.
        PermissionsOf(result).Should().BeEquivalentTo([Permissions.BillingRead]);
    }

    [Fact]
    public async Task Admin_owned_api_key_is_not_in_any_role_and_keeps_only_its_scoped_permission()
    {
        var principal = ApiKeyPrincipal([RoleConstants.Admin], [Permissions.BillingRead]);

        var result = await new PermissionClaimsTransformation().TransformAsync(principal);

        result.IsInRole(RoleConstants.Admin).Should().BeFalse("role checks must never match an API key");
        result.IsInRole(RoleConstants.SuperAdmin).Should().BeFalse();
        result.FindAll(ClaimTypes.Role).Should().BeEmpty();
        PermissionsOf(result).Should().BeEquivalentTo([Permissions.BillingRead]);
    }

    [Fact]
    public async Task Api_key_without_scopes_gets_no_permissions_even_for_a_super_admin()
    {
        var result = await new PermissionClaimsTransformation()
            .TransformAsync(ApiKeyPrincipal([RoleConstants.SuperAdmin], []));

        PermissionsOf(result).Should().BeEmpty();
    }

    [Fact]
    public async Task Non_api_key_principal_is_not_limited_by_scope_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, RoleConstants.Support), new Claim(ApiKeyClaimTypes.Scope, Permissions.BillingRead)],
            "Bearer"));

        var result = await new PermissionClaimsTransformation().TransformAsync(principal);

        PermissionsOf(result).Should().BeEquivalentTo(RolePermissionMap.For(RoleConstants.Support));
    }

    [Fact]
    public void Allows_is_only_a_restriction_for_api_key_principals()
    {
        var key = ApiKeyPrincipal([], [Permissions.BillingRead]);
        var bearer = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u")], "Bearer"));

        Shared.Core.Authorization.ApiKeyPrincipal.Allows(key, Permissions.BillingRead).Should().BeTrue();
        Shared.Core.Authorization.ApiKeyPrincipal.Allows(key, Permissions.UsersRead).Should().BeFalse();
        Shared.Core.Authorization.ApiKeyPrincipal.Allows(bearer, Permissions.UsersRead).Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_permission_of_a_membership_requires_the_scope_for_api_keys()
    {
        var options = new MultiTenancyOptions { Enabled = true };
        var tenant = TenantTestData.TenantA;
        var membership = new TenantMembershipInfo(
            tenant.Id, Guid.NewGuid(), false,
            new HashSet<string> { TenantPermissions.ProductsManage, TenantPermissions.MembersRead },
            BranchScope.Unrestricted, "stamp");
        var handler = new TenantPermissionHandler(
            new TenantContext(Microsoft.Extensions.Options.Options.Create(options)),
            Microsoft.Extensions.Options.Options.Create(options));

        async Task<bool> Succeeds(ClaimsPrincipal user, string permission)
        {
            var requirement = new TenantPermissionRequirement(permission);
            var ctx = new AuthorizationHandlerContext([requirement], user, null);
            await handler.HandleAsync(ctx);
            return ctx.HasSucceeded;
        }

        var key = ApiKeyPrincipal([], [TenantPermissions.ProductsManage]);

        using (TenantScope.Begin(tenant, membership: membership))
        {
            (await Succeeds(key, TenantPermissions.ProductsManage)).Should().BeTrue();
            (await Succeeds(key, TenantPermissions.MembersRead)).Should().BeFalse("held by the member but not granted to the key");
            (await Succeeds(key, TenantPermissions.Member)).Should().BeTrue("plain membership needs no scope");
        }
    }

    [Fact]
    public async Task Middleware_blocks_api_keys_on_restricted_endpoints_with_a_stable_error_code()
    {
        var called = false;
        var middleware = new ApiKeyRestrictionMiddleware(_ => { called = true; return Task.CompletedTask; });
        var http = new DefaultHttpContext { User = ApiKeyPrincipal([RoleConstants.User], []) };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new DisallowApiKeyMetadata()), "restricted"));

        var act = () => middleware.InvokeAsync(http);

        var ex = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        ex.ErrorCode.Should().Be(ApiKeyClaimTypes.RestrictedErrorCode);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Middleware_lets_api_keys_through_unrestricted_endpoints_and_bearer_through_restricted_ones()
    {
        var calls = 0;
        var middleware = new ApiKeyRestrictionMiddleware(_ => { calls++; return Task.CompletedTask; });

        var open = new DefaultHttpContext { User = ApiKeyPrincipal([], []) };
        open.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(), "open"));
        await middleware.InvokeAsync(open);

        var bearer = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u")], "Bearer"))
        };
        bearer.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new DisallowApiKeyMetadata()), "restricted"));
        await middleware.InvokeAsync(bearer);

        calls.Should().Be(2);
    }

    [Fact]
    public void Rate_limit_partition_of_an_api_key_is_the_key_not_the_user()
    {
        var http = new DefaultHttpContext { User = ApiKeyPrincipal([], []) };
        var keyId = http.User.FindFirstValue(ApiKeyClaimTypes.KeyId);

        Shared.Core.Extensions.RateLimitingExtensions.GetUserOrIpKey(http).Should().Be($"apikey:{keyId}");
    }
}
