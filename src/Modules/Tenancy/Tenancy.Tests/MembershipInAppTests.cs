using FluentAssertions;
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
}
