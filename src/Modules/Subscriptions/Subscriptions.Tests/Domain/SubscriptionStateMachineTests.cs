using FluentAssertions;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests.Domain;

public class SubscriptionStateMachineTests
{
    public static TheoryData<SubscriptionStatus, SubscriptionStatus, bool> Transitions()
    {
        var allowed = new HashSet<(SubscriptionStatus, SubscriptionStatus)>
        {
            (SubscriptionStatus.PendingApproval, SubscriptionStatus.Active),
            (SubscriptionStatus.PendingApproval, SubscriptionStatus.Rejected),
            (SubscriptionStatus.Trialing, SubscriptionStatus.PastDue),
            (SubscriptionStatus.Trialing, SubscriptionStatus.Cancelled),
            (SubscriptionStatus.Active, SubscriptionStatus.PastDue),
            (SubscriptionStatus.Active, SubscriptionStatus.Cancelled),
            (SubscriptionStatus.PastDue, SubscriptionStatus.Active),
            (SubscriptionStatus.PastDue, SubscriptionStatus.Expired),
            (SubscriptionStatus.PastDue, SubscriptionStatus.Cancelled)
        };

        var data = new TheoryData<SubscriptionStatus, SubscriptionStatus, bool>();
        foreach (var from in Enum.GetValues<SubscriptionStatus>())
            foreach (var to in Enum.GetValues<SubscriptionStatus>())
                data.Add(from, to, allowed.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Transition_table(SubscriptionStatus from, SubscriptionStatus to, bool expected)
    {
        SubscriptionStateMachine.CanTransition(from, to).Should().Be(expected);
    }

    [Fact]
    public void Status_numbers_are_stable()
    {
        ((int)SubscriptionStatus.PendingApproval).Should().Be(0);
        ((int)SubscriptionStatus.Active).Should().Be(1);
        ((int)SubscriptionStatus.PastDue).Should().Be(2);
        ((int)SubscriptionStatus.Expired).Should().Be(3);
        ((int)SubscriptionStatus.Cancelled).Should().Be(4);
        ((int)SubscriptionStatus.Rejected).Should().Be(5);
        ((int)SubscriptionStatus.Trialing).Should().Be(6, "appended, never renumbered");
    }

    [Fact]
    public void Open_statuses_are_pending_trialing_active_and_past_due()
    {
        SubscriptionStateMachine.OpenStatuses.Should().BeEquivalentTo(
            [SubscriptionStatus.PendingApproval, SubscriptionStatus.Trialing, SubscriptionStatus.Active, SubscriptionStatus.PastDue]);
    }

    public static TheoryData<SubscriptionStatus> AllStatuses()
    {
        var data = new TheoryData<SubscriptionStatus>();
        foreach (var status in Enum.GetValues<SubscriptionStatus>())
            data.Add(status);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Entity_activate_follows_the_table(SubscriptionStatus status) =>
        AssertEntityTransition(status, SubscriptionStatus.Active, s => s.Activate(SubscriptionFactory.DefaultNow.AddDays(1)),
            allowedFrom: [SubscriptionStatus.PendingApproval]);

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Entity_reject_follows_the_table(SubscriptionStatus status) =>
        AssertEntityTransition(status, SubscriptionStatus.Rejected, s => s.Reject("x", SubscriptionFactory.DefaultNow),
            allowedFrom: [SubscriptionStatus.PendingApproval]);

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Entity_mark_past_due_follows_the_table(SubscriptionStatus status) =>
        AssertEntityTransition(status, SubscriptionStatus.PastDue,
            s => s.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, SubscriptionFactory.Tax), 7, SubscriptionFactory.DefaultNow.AddMonths(2)),
            allowedFrom: [SubscriptionStatus.Active]);

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Entity_renew_follows_the_table(SubscriptionStatus status) =>
        AssertEntityTransition(status, SubscriptionStatus.Active, s => s.Renew(SubscriptionFactory.DefaultNow.AddMonths(1)),
            allowedFrom: [SubscriptionStatus.PastDue]);

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Entity_expire_follows_the_table(SubscriptionStatus status) =>
        AssertEntityTransition(status, SubscriptionStatus.Expired, s => s.Expire(SubscriptionFactory.DefaultNow.AddMonths(2)),
            allowedFrom: [SubscriptionStatus.PastDue]);

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Entity_cancel_immediately_follows_the_table(SubscriptionStatus status) =>
        AssertEntityTransition(status, SubscriptionStatus.Cancelled,
            s => s.CancelImmediately(Guid.NewGuid(), null, null, SubscriptionFactory.DefaultNow.AddDays(2)),
            allowedFrom: [SubscriptionStatus.Trialing, SubscriptionStatus.Active, SubscriptionStatus.PastDue]);

    private static void AssertEntityTransition(
        SubscriptionStatus from,
        SubscriptionStatus expectedTo,
        Action<Subscription> transition,
        SubscriptionStatus[] allowedFrom)
    {
        var subscription = SubscriptionFactory.InStatus(from);
        var act = () => transition(subscription);

        if (allowedFrom.Contains(from))
        {
            act.Should().NotThrow();
            subscription.Status.Should().Be(expectedTo);
        }
        else
        {
            act.Should().Throw<ConflictException>();
            subscription.Status.Should().Be(from);
        }
    }
}
