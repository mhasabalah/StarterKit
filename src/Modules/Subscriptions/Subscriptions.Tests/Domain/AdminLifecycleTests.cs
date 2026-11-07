using FluentAssertions;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Exceptions;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests.Domain;

/// <summary>State rules of the admin lifecycle actions: extend, comp, reactivate; plus duplicate-proof flags.</summary>
public class AdminLifecycleTests
{
    private static readonly DateTimeOffset Now = SubscriptionFactory.DefaultNow;
    private static readonly Guid Admin = Guid.NewGuid();
    private const int Window = 30;

    // ----- extend -----

    [Fact]
    public void Extending_an_active_subscription_moves_the_renewal_date_and_what_is_tied_to_it()
    {
        var subscription = SubscriptionFactory.Active(price: 100m);
        var renewal = subscription.RenewalDate!.Value;
        var downgrade = subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m), Guid.NewGuid(), Now);

        subscription.Extend(10, Now.AddDays(1));

        subscription.RenewalDate.Should().Be(renewal.AddDays(10));
        downgrade.EffectiveAt.Should().Be(renewal.AddDays(10));
        subscription.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void Extending_moves_a_scheduled_end_of_period_cancellation()
    {
        var subscription = SubscriptionFactory.Active();
        subscription.ScheduleCancellation(Guid.NewGuid(), null, null, Now);

        subscription.Extend(5, Now);

        subscription.ScheduledCancellationDate.Should().Be(subscription.RenewalDate);
    }

    [Fact]
    public void Extending_a_past_due_subscription_extends_the_grace_period_only()
    {
        var subscription = SubscriptionFactory.InStatus(SubscriptionStatus.PastDue);
        var renewal = subscription.RenewalDate;
        var grace = subscription.GracePeriodEndsAt!.Value;

        subscription.Extend(7, Now);

        subscription.GracePeriodEndsAt.Should().Be(grace.AddDays(7));
        subscription.RenewalDate.Should().Be(renewal, "the period was already billed");
    }

    [Theory]
    [InlineData(SubscriptionStatus.PendingApproval)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    [InlineData(SubscriptionStatus.Rejected)]
    public void Only_active_or_past_due_subscriptions_can_be_extended(SubscriptionStatus status)
    {
        var act = () => SubscriptionFactory.InStatus(status).Extend(5, Now);

        act.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(367)]
    public void Extension_days_are_bounded(int days)
    {
        var act = () => SubscriptionFactory.Active().Extend(days, Now);

        act.Should().Throw<BadRequestException>();
    }

    // ----- comp -----

    [Fact]
    public void Comp_activates_a_pending_subscription_for_n_months_with_a_paid_zero_payment()
    {
        var subscription = SubscriptionFactory.Pending(price: 100m);
        var plan = SubscriptionFactory.Plan(500m, cycle: 12, name: "Enterprise");

        var payment = subscription.ActivateComplimentary(plan, 3, Admin, Window, Now);

        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.PlanId.Should().Be(plan.PlanId);
        subscription.Price.Should().Be(500m, "the next renewal is billed at the plan price");
        subscription.StartDate.Should().Be(Now);
        subscription.RenewalDate.Should().Be(Now.AddMonths(3));
        subscription.ComplimentaryUntil.Should().Be(subscription.RenewalDate);
        payment.Should().BeEquivalentTo(new { Amount = 0m, Status = PaymentStatus.Paid, SubscriptionId = subscription.Id });
        subscription.Payments.Should().Contain(payment);
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Rejected)]
    public void Comp_is_refused_for_running_or_rejected_subscriptions(SubscriptionStatus status)
    {
        var act = () => SubscriptionFactory.InStatus(status).ActivateComplimentary(SubscriptionFactory.Plan(1m), 1, Admin, Window, Now);

        act.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public void Comp_months_are_bounded(int months)
    {
        var act = () => SubscriptionFactory.Pending().ActivateComplimentary(SubscriptionFactory.Plan(1m), months, Admin, Window, Now);

        act.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Comp_reopens_a_recently_cancelled_subscription_with_a_fresh_anchor()
    {
        var subscription = SubscriptionFactory.InStatus(SubscriptionStatus.Cancelled);
        var later = subscription.EndDate!.Value.AddDays(3);

        subscription.ActivateComplimentary(subscription.CurrentPlanSnapshot(), 2, Admin, Window, later);

        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.StartDate.Should().Be(later);
        subscription.EndDate.Should().BeNull();
        subscription.CancelledAt.Should().BeNull();
    }

    [Fact]
    public void The_renewal_boundary_ends_the_complimentary_period()
    {
        var subscription = SubscriptionFactory.Pending();
        subscription.ActivateComplimentary(SubscriptionFactory.Plan(100m), 1, Admin, Window, Now);

        subscription.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, SubscriptionFactory.Tax, "SAR"), 7, subscription.RenewalDate!.Value);

        subscription.ComplimentaryUntil.Should().BeNull();
    }

    // ----- reactivate -----

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    public void Reactivation_reopens_as_pending_with_a_new_activation_payment(SubscriptionStatus status)
    {
        var subscription = SubscriptionFactory.InStatus(status);
        var at = subscription.EndDate!.Value.AddDays(1);

        var payment = subscription.Reactivate(120m, 138m, Window, at);

        subscription.Status.Should().Be(SubscriptionStatus.PendingApproval);
        subscription.Price.Should().Be(120m);
        subscription.StartDate.Should().BeNull();
        subscription.RenewalDate.Should().BeNull();
        subscription.EndDate.Should().BeNull();
        subscription.GracePeriodEndsAt.Should().BeNull();
        subscription.CancellationReason.Should().BeNull();
        payment.Should().BeEquivalentTo(new
        {
            Purpose = PaymentPurpose.Activation,
            Status = PaymentStatus.Pending,
            Amount = 138m,
            SubscriptionId = subscription.Id
        });

        subscription.Activate(at.AddDays(1));
        subscription.StartDate.Should().Be(at.AddDays(1), "activation after reactivation starts a fresh billing anchor");
    }

    [Fact]
    public void Reactivation_outside_the_window_is_refused()
    {
        var subscription = SubscriptionFactory.InStatus(SubscriptionStatus.Cancelled);

        var act = () => subscription.Reactivate(100m, 115m, Window, subscription.EndDate!.Value.AddDays(Window + 1));

        act.Should().Throw<ConflictException>().WithMessage("*more than 30 days*");
    }

    [Theory]
    [InlineData(SubscriptionStatus.PendingApproval)]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.PastDue)]
    [InlineData(SubscriptionStatus.Rejected)]
    public void Only_cancelled_or_expired_subscriptions_can_be_reactivated(SubscriptionStatus status)
    {
        var act = () => SubscriptionFactory.InStatus(status).Reactivate(100m, 115m, Window, Now);

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Reactivation_is_not_a_regular_state_machine_transition()
    {
        SubscriptionStateMachine.CanTransition(SubscriptionStatus.Cancelled, SubscriptionStatus.PendingApproval).Should().BeFalse();
        SubscriptionStateMachine.CanReactivate(SubscriptionStatus.Cancelled).Should().BeTrue();
        SubscriptionStateMachine.CanReactivate(SubscriptionStatus.Expired).Should().BeTrue();
        SubscriptionStateMachine.CanReactivate(SubscriptionStatus.Rejected).Should().BeFalse();
    }

    // ----- subscriber snapshot -----

    [Fact]
    public void Subscriber_snapshot_keeps_previous_values_when_the_directory_has_none()
    {
        var subscription = SubscriptionFactory.Pending();
        subscription.UpdateSubscriberSnapshot("Acme Ltd", "billing@acme.test");

        subscription.UpdateSubscriberSnapshot(null, " ");

        subscription.SubscriberDisplayName.Should().Be("Acme Ltd");
        subscription.SubscriberEmail.Should().Be("billing@acme.test");
    }

    // ----- duplicate proof flag -----

    [Fact]
    public void A_proof_with_a_known_hash_is_flagged_but_still_submitted()
    {
        var payment = SubscriptionFactory.Pending().ActivationPayment;
        var other = Guid.NewGuid();

        payment.SubmitProof(Guid.NewGuid(), Method(), Guid.NewGuid(), Now, new string('a', 64), other);

        payment.Status.Should().Be(PaymentStatus.Submitted);
        payment.DuplicateProof.Should().BeTrue();
        payment.DuplicateOfPaymentId.Should().Be(other);
        payment.ProofContentHash.Should().Be(new string('a', 64));
    }

    [Fact]
    public void A_new_unique_proof_clears_a_previous_duplicate_flag()
    {
        var payment = SubscriptionFactory.Pending().ActivationPayment;
        payment.SubmitProof(Guid.NewGuid(), Method(), Guid.NewGuid(), Now, new string('a', 64), Guid.NewGuid());
        payment.Reject(Admin, "duplicate", Guid.NewGuid(), Now);

        payment.SubmitProof(Guid.NewGuid(), Method(), Guid.NewGuid(), Now, new string('b', 64));

        payment.DuplicateProof.Should().BeFalse();
        payment.DuplicateOfPaymentId.Should().BeNull();
        payment.ProofContentHash.Should().Be(new string('b', 64));
    }

    private static PaymentMethodSnapshot Method() => new(Guid.NewGuid(), "BankTransfer", "Bank", "ACC", null);
}
