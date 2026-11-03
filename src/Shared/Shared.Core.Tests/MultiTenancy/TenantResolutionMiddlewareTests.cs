using MsOptions = Microsoft.Extensions.Options.Options;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.MultiTenancy.Web;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.MultiTenancy;

public sealed class TenantResolutionMiddlewareTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly FakeStore _store = new(TenantA, TenantB);
    private readonly FakeMemberships _memberships = new();

    private MultiTenancyOptions _options = new()
    {
        Enabled = true,
        Resolution = new TenantResolutionOptions { RootDomain = "api.example.com" }
    };

    private async Task<(TenantScopeState? Scope, HttpContext Http)> RunAsync(Action<HttpContext> arrange,
        TenantRequirement? requirement = TenantRequirement.Required)
    {
        TenantScopeState? captured = null;
        var middleware = new TenantResolutionMiddleware(_ =>
        {
            captured = TenantScope.Current;
            return Task.CompletedTask;
        }, MsOptions.Create(_options), NullLogger<TenantResolutionMiddleware>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Host = new HostString("api.example.com");
        http.Request.Method = "GET";
        if (requirement is { } r)
        {
            http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new TenantRequirementMetadata(r)), "test"));
        }

        arrange(http);
        await middleware.InvokeAsync(http, _store, _memberships, new TenantDataContextRegistry());
        return (captured, http);
    }

    private static void SignIn(HttpContext http, Guid? tenantId = null, string? stamp = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, UserId.ToString()) };
        if (tenantId is { } tid) claims.Add(new Claim(TenantClaimTypes.TenantId, tid.ToString()));
        if (stamp is not null) claims.Add(new Claim(TenantClaimTypes.MembershipStamp, stamp));
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task Token_claim_selects_the_tenant_and_loads_the_membership()
    {
        _memberships.Add(UserId, TenantA.Id, "s1");
        var (scope, _) = await RunAsync(h => SignIn(h, TenantA.Id, "s1"));

        scope!.Tenant.Should().Be(TenantA);
        scope.Membership!.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Header_naming_another_tenant_than_the_token_is_rejected()
    {
        _memberships.Add(UserId, TenantA.Id, "s1");
        var act = () => RunAsync(h =>
        {
            SignIn(h, TenantA.Id, "s1");
            h.Request.Headers["X-Tenant"] = TenantB.Identifier;
        });

        (await act.Should().ThrowAsync<TenantAccessDeniedException>()).Which.Error.Should().Be(TenantErrors.TenantMismatch);
    }

    [Fact]
    public async Task Removed_member_with_an_old_token_is_rejected()
    {
        var act = () => RunAsync(h => SignIn(h, TenantA.Id, "s1"));
        (await act.Should().ThrowAsync<TenantAccessDeniedException>()).Which.Error.Should().Be(TenantErrors.NotAMember);
    }

    [Fact]
    public async Task Changed_membership_makes_the_token_stale()
    {
        _memberships.Add(UserId, TenantA.Id, "new-stamp");
        var act = () => RunAsync(h => SignIn(h, TenantA.Id, "old-stamp"));
        await act.Should().ThrowAsync<StaleTenantMembershipException>();
    }

    [Fact]
    public async Task Anonymous_request_resolves_the_tenant_from_the_subdomain()
    {
        var (scope, _) = await RunAsync(h => h.Request.Host = new HostString("acme.api.example.com"));
        scope!.Tenant.Should().Be(TenantA);
        scope.Membership.Should().BeNull();
    }

    [Theory]
    [InlineData("www.api.example.com")]
    [InlineData("api.example.com")]
    public async Task Reserved_and_root_hosts_are_not_tenants(string host)
    {
        var act = () => RunAsync(h => h.Request.Host = new HostString(host));
        await act.Should().ThrowAsync<TenantNotResolvedException>();
    }

    [Fact]
    public async Task Header_by_id_or_identifier_works_for_anonymous_callers()
    {
        (await RunAsync(h => h.Request.Headers["X-Tenant"] = TenantB.Id.ToString())).Scope!.Tenant.Should().Be(TenantB);
        (await RunAsync(h => h.Request.Headers["X-Tenant"] = "globex")).Scope!.Tenant.Should().Be(TenantB);
    }

    [Fact]
    public async Task Unknown_tenant_is_not_found()
    {
        var act = () => RunAsync(h => h.Request.Headers["X-Tenant"] = "nope");
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Optional_endpoints_ignore_an_invalid_tenant()
    {
        var (scope, _) = await RunAsync(h => h.Request.Headers["X-Tenant"] = "nope", TenantRequirement.Optional);
        scope!.Tenant.Should().BeNull();
    }

    [Fact]
    public async Task Host_endpoints_never_run_inside_a_tenant()
    {
        _memberships.Add(UserId, TenantA.Id, "s1");
        var (scope, _) = await RunAsync(h => SignIn(h, TenantA.Id, "s1"), TenantRequirement.Host);
        scope!.Tenant.Should().BeNull();
        scope.IsHost.Should().BeFalse("host endpoints are tenant-less, not host-scoped");
    }

    [Fact]
    public async Task Suspended_tenant_is_blocked()
    {
        _store.Replace(TenantA with { Status = TenantStatus.Suspended });
        var act = () => RunAsync(h => h.Request.Headers["X-Tenant"] = "acme");
        (await act.Should().ThrowAsync<TenantAccessDeniedException>()).Which.Error.Should().Be(TenantErrors.TenantSuspended);
    }

    [Fact]
    public async Task Suspended_tenant_in_read_only_mode_allows_reads_only()
    {
        _options.SuspendedTenantMode = SuspendedTenantMode.ReadOnly;
        _store.Replace(TenantA with { Status = TenantStatus.Suspended });

        (await RunAsync(h => h.Request.Headers["X-Tenant"] = "acme")).Scope!.Tenant!.Id.Should().Be(TenantA.Id);

        var write = () => RunAsync(h =>
        {
            h.Request.Headers["X-Tenant"] = "acme";
            h.Request.Method = "POST";
        });
        await write.Should().ThrowAsync<TenantAccessDeniedException>();
    }

    [Fact]
    public async Task Branch_header_outside_the_scope_is_rejected()
    {
        var allowed = Guid.NewGuid();
        _memberships.Add(UserId, TenantA.Id, "s1", new BranchScope([allowed]));

        (await RunAsync(h =>
        {
            SignIn(h, TenantA.Id, "s1");
            h.Request.Headers["X-Branch"] = allowed.ToString();
        })).Scope!.Branches.ActiveBranchId.Should().Be(allowed);

        var act = () => RunAsync(h =>
        {
            SignIn(h, TenantA.Id, "s1");
            h.Request.Headers["X-Branch"] = Guid.NewGuid().ToString();
        });
        (await act.Should().ThrowAsync<TenantAccessDeniedException>()).Which.Error.Should().Be(TenantErrors.BranchNotAllowed);
    }

    [Fact]
    public async Task Single_tenant_mode_ignores_tenant_inputs_and_uses_the_default_tenant()
    {
        _options = new MultiTenancyOptions { Enabled = false };
        var (scope, http) = await RunAsync(h => h.Request.Headers["X-Tenant"] = "globex");
        scope!.Tenant.Should().Be(TenantDefaults.DefaultTenant);
        http.Items[typeof(TenantInfo)].Should().Be(TenantDefaults.DefaultTenant);
    }

    [Fact]
    public async Task Migrating_tenant_serves_reads_and_defers_writes_with_503()
    {
        _store.Replace(TenantA with { Status = TenantStatus.Migrating });

        var (scope, _) = await RunAsync(h => h.Request.Host = new HostString("acme.api.example.com"));
        scope!.Tenant.Id.Should().Be(TenantA.Id);

        var write = () => RunAsync(h =>
        {
            h.Request.Host = new HostString("acme.api.example.com");
            h.Request.Method = "POST";
        });
        (await write.Should().ThrowAsync<TenantMigratingException>()).Which.StatusCode
            .Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    // ----- fakes -----

    private sealed class FakeStore(params TenantInfo[] tenants) : ITenantStore
    {
        private readonly List<TenantInfo> _tenants = [.. tenants];

        public void Replace(TenantInfo tenant)
        {
            _tenants.RemoveAll(t => t.Id == tenant.Id);
            _tenants.Add(tenant);
        }

        public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tenants.FirstOrDefault(t => t.Id == tenantId));

        public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tenants.FirstOrDefault(t => t.Identifier == identifier));

        public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantInfo?>(null);

        public Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants);
    }

    private sealed class FakeMemberships : ITenantMembershipResolver
    {
        private readonly Dictionary<(Guid, Guid), TenantMembershipInfo> _rows = [];

        public void Add(Guid userId, Guid tenantId, string stamp, BranchScope? branches = null) =>
            _rows[(userId, tenantId)] = new TenantMembershipInfo(tenantId, userId, false,
                new HashSet<string>(), branches ?? BranchScope.Unrestricted, stamp);

        public Task<TenantMembershipInfo?> GetMembershipAsync(Guid userId, Guid tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.GetValueOrDefault((userId, tenantId)));

        public Task<IReadOnlyList<Guid>> GetTenantIdsOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(_rows.Keys.Where(k => k.Item1 == userId).Select(k => k.Item2).ToList());

        public Task InvalidateAsync(Guid? tenantId = null, Guid? userId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

public sealed class TenantPermissionHandlerTests
{
    private static async Task<bool> AuthorizeAsync(MultiTenancyOptions options, ClaimsPrincipal user, string permission)
    {
        var handler = new TenantPermissionHandler(new TenantContext(MsOptions.Create(options)), MsOptions.Create(options));
        var requirement = new TenantPermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], user, null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static TenantMembershipInfo Membership(bool owner, params string[] permissions) =>
        new(TenantA.Id, Guid.NewGuid(), owner, new HashSet<string>(permissions), BranchScope.Unrestricted, "s");

    private static readonly ClaimsPrincipal PlatformAdmin = new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(Permissions.ClaimType, TenantPermissions.MembersManage)
    ], "test"));

    [Fact]
    public async Task Membership_permissions_grant_access()
    {
        var options = new MultiTenancyOptions { Enabled = true };
        using (TenantScope.Begin(TenantA, membership: Membership(false, TenantPermissions.MembersManage)))
        {
            (await AuthorizeAsync(options, PlatformAdmin, TenantPermissions.MembersManage)).Should().BeTrue();
            (await AuthorizeAsync(options, PlatformAdmin, TenantPermissions.RolesManage)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Owners_hold_every_permission()
    {
        using (TenantScope.Begin(TenantA, membership: Membership(true)))
        {
            (await AuthorizeAsync(new MultiTenancyOptions { Enabled = true }, PlatformAdmin, TenantPermissions.RolesManage))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task Platform_claims_never_grant_tenant_permissions_in_multi_tenant_mode()
    {
        using (TenantScope.Begin(TenantA))
        {
            (await AuthorizeAsync(new MultiTenancyOptions { Enabled = true }, PlatformAdmin, TenantPermissions.MembersManage))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task Platform_claims_manage_the_default_tenant_in_single_tenant_mode() =>
        (await AuthorizeAsync(new MultiTenancyOptions(), PlatformAdmin, TenantPermissions.MembersManage)).Should().BeTrue();
}
