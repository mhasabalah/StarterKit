using Identity.Contracts.Sso;
using Identity.Domains.Users.Models;
using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Events;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Identity.Services.Sso;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shared.Contracts.Mediation;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Caching;
using Tenancy.Contracts.Roles;

namespace Identity.Tests.Sso;

/// <summary>
/// Enforcement lapses with the plan, discovery-cache eviction reaches every replica (old AND new authority), and
/// directory role assignments only accept roles of the current tenant.
/// </summary>
public class SsoPolicyTests
{
    private const string Domain = "acme.example";
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static SsoConnection EnforcingConnection()
    {
        var connection = SsoConnection.Create(Guid.NewGuid(), new SsoConnectionInput
        {
            Name = "Acme",
            Protocol = SsoProtocol.Oidc,
            OidcAuthority = "https://idp.acme.example",
            OidcClientId = "client",
            EnforceSso = true
        }, "protected-secret");
        connection.AddDomain(Domain);
        connection.MarkDomainVerified(Domain, Now);
        connection.Activate();
        connection.ClearDomainEvents();
        return connection;
    }

    private static (SsoEnforcementPolicy Policy, ApplicationUser User) Policy(SsoConnection connection, bool entitled)
    {
        var repository = new Mock<ISsoConnectionRepository>();
        repository.Setup(r => r.FindActiveByVerifiedDomainAsync(Domain, It.IsAny<CancellationToken>())).ReturnsAsync(connection);

        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = $"ada@{Domain}", UserName = $"ada@{Domain}" };
        var users = Mocks.UserManager();
        users.WithUsers((user, ["User"]));

        var entitlement = new Mock<ISsoEntitlement>();
        entitlement.Setup(e => e.IsEntitledAsync(connection.TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(entitled);

        var policy = new SsoEnforcementPolicy(repository.Object, users.Object, Mock.Of<ITenantMembershipResolver>(),
            entitlement.Object, Microsoft.Extensions.Options.Options.Create(new SsoOptions()));
        return (policy, user);
    }

    [Fact]
    public async Task An_entitled_enforcing_tenant_blocks_password_sign_in_and_reset()
    {
        var connection = EnforcingConnection();
        var (policy, user) = Policy(connection, entitled: true);

        var act = () => policy.EnsureNonSsoSignInAllowedAsync(user);

        (await act.Should().ThrowAsync<SsoRequiredException>()).Which.ConnectionId.Should().Be(connection.Id);
        (await policy.IsPasswordResetAllowedAsync(user)).Should().BeFalse();
    }

    [Fact]
    public async Task A_tenant_whose_plan_lost_sso_falls_back_to_passwords_instead_of_locking_users_out()
    {
        var (policy, user) = Policy(EnforcingConnection(), entitled: false);

        await policy.Invoking(p => p.EnsureNonSsoSignInAllowedAsync(user)).Should().NotThrowAsync();
        (await policy.IsPasswordResetAllowedAsync(user)).Should().BeTrue();
        (await policy.FindConnectionForEmailAsync(user.Email)).Should().BeNull("discovery must not offer an unusable SSO");
    }

    [Fact]
    public void Changing_the_authority_reports_the_old_one_so_its_cached_discovery_is_dropped()
    {
        var connection = EnforcingConnection();

        connection.Update(new SsoConnectionInput
        {
            Name = "Acme",
            Protocol = SsoProtocol.Oidc,
            OidcAuthority = "https://new-idp.acme.example/",
            OidcClientId = "client",
            EnforceSso = true
        }, null);

        var changed = connection.DomainEvents.OfType<SsoConnectionChangedEvent>().Should().ContainSingle().Subject;
        changed.OidcAuthority.Should().Be("https://new-idp.acme.example");
        changed.PreviousOidcAuthority.Should().Be("https://idp.acme.example");
    }

    [Fact]
    public void A_status_change_keeps_the_same_authority_without_a_previous_one()
    {
        var connection = EnforcingConnection();

        connection.Disable();

        var changed = connection.DomainEvents.OfType<SsoConnectionChangedEvent>().Should().ContainSingle().Subject;
        changed.OidcAuthority.Should().Be("https://idp.acme.example");
        changed.PreviousOidcAuthority.Should().BeNull();
    }

    [Fact]
    public async Task Forgetting_an_authority_evicts_locally_and_broadcasts_to_every_replica()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var bus = new Mock<ILocalCacheInvalidationBus>();
        var cache = new OidcConfigurationCache(Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()),
            Microsoft.Extensions.Options.Options.Create(new SsoOptions()), memory, bus.Object);
        cache.Refresh("https://idp.acme.example"); // creates the cached manager without network I/O
        memory.TryGetValue(OidcConfigurationCache.CacheKey("https://idp.acme.example"), out _).Should().BeTrue();

        await cache.ForgetAsync(["https://IDP.acme.example/", "https://old.acme.example", null], CancellationToken.None);

        memory.TryGetValue(OidcConfigurationCache.CacheKey("https://idp.acme.example"), out _).Should().BeFalse();
        bus.Verify(b => b.PublishAsync(
            It.Is<IReadOnlyCollection<string>>(keys => keys.Count == 2 &&
                keys.Contains(OidcConfigurationCache.CacheKey("https://idp.acme.example")) &&
                keys.Contains(OidcConfigurationCache.CacheKey("https://old.acme.example"))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Role_ids_must_be_roles_of_the_current_tenant()
    {
        var known = Guid.NewGuid();
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<GetTenantRolesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TenantRoleDto(known, "Member", null, true, [], 0, 1)]);
        var guard = new TenantRoleGuard(sender.Object);

        await guard.Invoking(g => g.EnsureTenantRolesAsync([known], CancellationToken.None)).Should().NotThrowAsync();
        var foreign = Guid.NewGuid();
        (await guard.Invoking(g => g.EnsureTenantRolesAsync([known, foreign], CancellationToken.None))
                .Should().ThrowAsync<UnknownTenantRoleException>())
            .Which.ErrorCode.Should().Be("tenant_role_not_found");
    }

    [Fact]
    public async Task No_roles_need_no_lookup()
    {
        var sender = new Mock<ISender>(MockBehavior.Strict);

        await new TenantRoleGuard(sender.Object).EnsureTenantRolesAsync([Guid.Empty], CancellationToken.None);
    }
}
