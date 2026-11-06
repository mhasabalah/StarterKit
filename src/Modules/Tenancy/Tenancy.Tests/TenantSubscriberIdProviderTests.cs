using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web;
using Tenancy.Services;
using Xunit;

namespace Tenancy.Tests;

/// <summary>
/// Managing the subscription needs the billing permission; READING the plan (feature gates, quotas) does not: any
/// member's request, and background work for them, is paid by the tenant.
/// </summary>
public sealed class TenantSubscriberIdProviderTests
{
    private static readonly TenantInfo Tenant = new(Guid.NewGuid(), "acme", "Acme");

    private static TenantSubscriberIdProvider Provider(bool multiTenant, TenantMembershipInfo? membership, Guid? httpUser = null)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(c => c.TenantId).Returns(Tenant.Id);
        context.SetupGet(c => c.Membership).Returns(membership);
        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserIdAsGuid()).Returns(httpUser);
        return new TenantSubscriberIdProvider(context.Object, http.Object,
            Options.Create(new MultiTenancyOptions { Enabled = multiTenant }));
    }

    private static TenantMembershipInfo Member(params string[] permissions) =>
        new(Tenant.Id, Guid.NewGuid(), false, permissions.ToHashSet(StringComparer.Ordinal), BranchScope.Unrestricted, "s");

    [Fact]
    public void A_member_without_billing_cannot_manage_the_subscription_but_the_tenant_pays_for_their_requests()
    {
        var provider = Provider(multiTenant: true, Member(TenantPermissions.DataImport));

        ((Action)(() => provider.GetCurrentSubscriberId())).Should().Throw<ForbiddenException>();
        provider.GetPayingSubscriberId().Should().Be(Tenant.Id);
    }

    [Fact]
    public void Background_work_without_a_membership_is_paid_by_the_tenant() =>
        Provider(multiTenant: true, membership: null).GetPayingSubscriberId().Should().Be(Tenant.Id);

    [Fact]
    public void Single_tenant_the_signed_in_or_acting_user_pays()
    {
        var user = Guid.NewGuid();

        Provider(multiTenant: false, membership: null, httpUser: user).GetPayingSubscriberId().Should().Be(user);
    }
}
