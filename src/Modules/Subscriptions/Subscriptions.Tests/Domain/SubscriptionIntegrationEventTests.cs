using FluentAssertions;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Events;
using Subscriptions.Contracts.Subscriptions.IntegrationEvents;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests.Domain;

/// <summary>
/// The aggregate announces billing-relevant facts to other modules (tenant suspension...) as integration events,
/// ALONGSIDE its in-process domain events. Exactly once per transition; no event for transitions nobody reacts to.
/// </summary>
public class SubscriptionIntegrationEventTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static void ShouldHaveSingleActivated(Subscription subscription)
    {
        var evt = subscription.IntegrationEvents.Should().ContainSingle().Subject
            .Should().BeOfType<SubscriptionActivatedIntegrationEvent>().Subject;
        evt.SubscriptionId.Should().Be(subscription.Id);
        evt.SubscriberId.Should().Be(subscription.SubscriberId);
        evt.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Creating_a_subscription_announces_nothing()
    {
        SubscriptionFactory.Pending(now: Start).IntegrationEvents.Should().BeEmpty();
    }

    [Fact]
    public void Activation_adds_one_activated_event_next_to_the_domain_event()
    {
        var subscription = SubscriptionFactory.Pending(now: Start);

        subscription.Activate(Start);

        ShouldHaveSingleActivated(subscription);
        subscription.DomainEvents.OfType<SubscriptionActivatedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Complimentary_activation_adds_one_activated_event()
    {
        var subscription = SubscriptionFactory.Pending(now: Start);

        subscription.ActivateComplimentary(SubscriptionFactory.Plan(100m), months: 3, adminId: Guid.NewGuid(),
            windowDays: 30, now: Start);

        ShouldHaveSingleActivated(subscription);
        subscription.DomainEvents.OfType<SubscriptionActivatedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Complimentary_reactivation_of_an_expired_subscription_adds_one_activated_event()
    {
        var subscription = SubscriptionFactory.InStatus(SubscriptionStatus.Expired);
        subscription.ClearIntegrationEvents();

        subscription.ActivateComplimentary(SubscriptionFactory.Plan(100m), months: 1, adminId: Guid.NewGuid(),
            windowDays: 3650, now: subscription.EndDate!.Value.AddDays(1));

        ShouldHaveSingleActivated(subscription);
    }

    [Fact]
    public void Expiry_adds_one_expired_event_next_to_the_domain_event()
    {
        var subscription = SubscriptionFactory.Active(now: Start);
        subscription.MarkPastDue(PricingPolicy.RenewalCharge(subscription.Price, 0m, SubscriptionFactory.Tax, "SAR"), 7,
            subscription.RenewalDate!.Value);
        subscription.ClearIntegrationEvents();

        subscription.Expire(subscription.GracePeriodEndsAt!.Value);

        var evt = subscription.IntegrationEvents.Should().ContainSingle().Subject
            .Should().BeOfType<SubscriptionExpiredIntegrationEvent>().Subject;
        evt.SubscriptionId.Should().Be(subscription.Id);
        evt.SubscriberId.Should().Be(subscription.SubscriberId);
        subscription.DomainEvents.OfType<SubscriptionExpiredEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Transitions_nobody_reacts_to_add_no_integration_event()
    {
        var active = SubscriptionFactory.Active(now: Start);
        active.ClearIntegrationEvents();

        active.MarkPastDue(PricingPolicy.RenewalCharge(active.Price, 0m, SubscriptionFactory.Tax, "SAR"), 7, active.RenewalDate!.Value);
        active.IntegrationEvents.Should().BeEmpty("past due is not announced");

        active.Renew(active.RenewalDate!.Value.AddDays(1));
        active.IntegrationEvents.Should().BeEmpty("a renewal keeps the subscription active: nothing changed for tenants");

        var cancelled = SubscriptionFactory.Active(now: Start);
        cancelled.ClearIntegrationEvents();
        cancelled.CancelImmediately(Guid.NewGuid(), null, null, Start.AddDays(1));
        cancelled.IntegrationEvents.Should().BeEmpty();

        var rejected = SubscriptionFactory.Pending(now: Start);
        rejected.Reject("no", Start);
        rejected.IntegrationEvents.Should().BeEmpty();
    }

    [Fact]
    public void Each_event_gets_its_own_id()
    {
        var first = SubscriptionFactory.Active(now: Start);
        var second = SubscriptionFactory.Active(now: Start);

        first.IntegrationEvents.Single().EventId.Should().NotBe(second.IntegrationEvents.Single().EventId);
    }
}
