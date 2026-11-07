using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Tenancy.Contracts.Events;
using Tenancy.Services;
using Xunit;

namespace Tenancy.Tests;

public class MembershipInAppTests
{
    [Theory]
    [InlineData("joined")]
    [InlineData("role-changed")]
    public void Membership_notifications_use_the_membership_category_and_both_languages(string key)
    {
        var tenantId = Guid.NewGuid();

        var content = MembershipInApp.Content(key, "Acme", tenantId);

        content.Category.Should().Be($"membership.{key}");
        content.Data!["tenantId"].Should().Be(tenantId.ToString());
        content.Body.En.Should().Contain("Acme");
        content.Body.Ar.Should().Contain("Acme").And.NotBe(content.Body.En);
        content.Title.Ar.Should().NotBe(content.Title.En);
    }

    [Fact]
    public void Only_a_role_change_flags_RolesChanged_on_the_membership_event()
    {
        var m = Tenancy.Domains.Members.Models.TenantMembership.Create(
            Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], [], DateTime.UtcNow);
        m.ClearDomainEvents();

        m.SetRoles([Guid.NewGuid()]);
        m.Suspend();

        m.DomainEvents.OfType<Tenancy.Contracts.Events.TenantMembershipChangedEvent>().Select(e => e.RolesChanged)
            .Should().Equal(true, false);
    }

    [Fact]
    public void Setting_the_same_roles_again_does_not_flag_a_role_change()
    {
        var role = Guid.NewGuid();
        var m = Tenancy.Domains.Members.Models.TenantMembership.Create(Guid.NewGuid(), Guid.NewGuid(), [role], [], DateTime.UtcNow);
        m.ClearDomainEvents();

        m.SetRoles([role]);

        m.DomainEvents.OfType<TenantMembershipChangedEvent>().Single().RolesChanged.Should().BeFalse();
    }

    private sealed class Fixture
    {
        public Mock<ITenantStore> Tenants { get; } = new();
        public Mock<IHttpContextService> Http { get; } = new();
        public Mock<INotificationSender> Sender { get; } = new();
        public Mock<INotificationBuilder> Builder { get; } = new();
        public TenantInfo? SeenScope { get; private set; }
        public InAppContent? Sent { get; private set; }
        public Guid? SentTo { get; private set; }

        public Fixture(TenantInfo? tenant)
        {
            Tenants.Setup(t => t.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
            Sender.Setup(s => s.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>())).Returns(Builder.Object)
                .Callback<Guid, NotificationCulture>((id, _) => SentTo = id);
            Builder.Setup(b => b.WithInApp(It.IsAny<InAppContent>())).Returns(Builder.Object)
                .Callback<InAppContent>(c => Sent = c);
            Builder.Setup(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    SeenScope = TenantScope.Current?.Tenant;
                    return Task.FromResult(Guid.NewGuid());
                });
        }

        public TenantMemberRolesChangedNotificationEventHandler RolesHandler() =>
            new(Tenants.Object, Http.Object, Sender.Object, NullLogger<TenantMemberRolesChangedNotificationEventHandler>.Instance);
    }

    [Fact]
    public async Task A_role_change_notifies_the_member_inside_the_tenant_scope()
    {
        var tenant = new TenantInfo(Guid.NewGuid(), "acme", "Acme");
        var member = Guid.NewGuid();
        var f = new Fixture(tenant);
        f.Http.Setup(h => h.GetUserIdAsGuid()).Returns(Guid.NewGuid()); // an admin made the change

        await f.RolesHandler().Handle(new TenantMembershipChangedEvent(tenant.Id, member, Removed: false) { RolesChanged = true }, default);

        f.SentTo.Should().Be(member);
        f.Sent!.Category.Should().Be("membership.role-changed");
        f.SeenScope.Should().Be(tenant);
    }

    [Fact]
    public async Task Other_membership_changes_and_removals_stay_silent()
    {
        var f = new Fixture(new TenantInfo(Guid.NewGuid(), "acme", "Acme"));

        await f.RolesHandler().Handle(new TenantMembershipChangedEvent(Guid.NewGuid(), Guid.NewGuid(), Removed: false), default);
        await f.RolesHandler().Handle(new TenantMembershipChangedEvent(Guid.NewGuid(), Guid.NewGuid(), Removed: true) { RolesChanged = true }, default);

        f.Sender.Verify(s => s.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>()), Times.Never);
    }

    [Fact]
    public async Task A_member_is_not_notified_about_their_own_change()
    {
        var member = Guid.NewGuid();
        var f = new Fixture(new TenantInfo(Guid.NewGuid(), "acme", "Acme"));
        f.Http.Setup(h => h.GetUserIdAsGuid()).Returns(member);

        await f.RolesHandler().Handle(new TenantMembershipChangedEvent(Guid.NewGuid(), member, Removed: false) { RolesChanged = true }, default);

        f.Sender.Verify(s => s.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>()), Times.Never);
    }

    [Fact]
    public async Task An_unknown_tenant_is_skipped()
    {
        var f = new Fixture(null);

        await f.RolesHandler().Handle(new TenantMembershipChangedEvent(Guid.NewGuid(), Guid.NewGuid(), Removed: false) { RolesChanged = true }, default);

        f.Sender.Verify(s => s.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>()), Times.Never);
    }
}
