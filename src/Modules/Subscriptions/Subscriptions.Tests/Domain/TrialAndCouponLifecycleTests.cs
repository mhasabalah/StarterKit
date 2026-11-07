using Shared.Core.Exceptions;
using FluentAssertions;
using Xunit;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Exceptions;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.Tests.Domain;

public class TrialAndCouponLifecycleTests
{
    private static readonly DateTimeOffset Now = SubscriptionFactory.DefaultNow;
    private static readonly TaxPolicy Tax = SubscriptionFactory.Tax;

    private static Subscription Trial(int days = 14, decimal price = 100m) =>
        Subscription.StartTrial(Guid.NewGuid(), SubscriptionFactory.Plan(price), days, Now);

    private static CouponTerms Coupon(int? periods, decimal percent = 10m) =>
        new(Guid.NewGuid(), "SAVE10", CouponDiscountType.Percent, percent, periods);

    [Fact]
    public void StartTrial_is_trialing_with_full_access_and_no_payment()
    {
        var trial = Trial(14);

        trial.Status.Should().Be(SubscriptionStatus.Trialing);
        trial.Payments.Should().BeEmpty();
        trial.TrialStartedAt.Should().Be(Now);
        trial.TrialEndsAt.Should().Be(Now.AddDays(14));
        trial.RenewalDate.Should().Be(trial.TrialEndsAt, "the trial end is the billing boundary");
        trial.IntegrationEvents.Should().ContainSingle("tenancy is told the subscriber is active");
        SubscriptionStateMachine.IsOpen(trial.Status).Should().BeTrue("a trial counts as the one open subscription");
    }

    [Fact]
    public void StartTrial_with_zero_days_is_rejected() =>
        FluentActions.Invoking(() => Trial(0)).Should().Throw<ConflictException>();

    [Fact]
    public void EndTrial_bills_the_first_paid_period_and_restarts_the_billing_anchor_at_the_trial_end()
    {
        var trial = Trial(14);
        var end = trial.TrialEndsAt!.Value;

        var payment = trial.EndTrial(PricingPolicy.RenewalCharge(100m, 0m, Tax, "SAR"), 3, end);

        trial.Status.Should().Be(SubscriptionStatus.PastDue);
        trial.GracePeriodEndsAt.Should().Be(end.AddDays(3));
        trial.StartDate.Should().Be(end);
        payment.Purpose.Should().Be(PaymentPurpose.Renewal);
        payment.Amount.Should().Be(100m);

        trial.Renew(end.AddDays(1));

        trial.Status.Should().Be(SubscriptionStatus.Active);
        trial.CurrentPeriodStart.Should().Be(end);
        trial.RenewalDate.Should().Be(end.AddMonths(1), "the paid cycle starts when the trial ended");
    }

    [Fact]
    public void A_trial_can_be_cancelled_and_cannot_be_activated_like_a_pending_subscription()
    {
        var trial = Trial();

        FluentActions.Invoking(() => trial.Activate(Now)).Should().Throw<InvalidSubscriptionTransitionException>();
        trial.CancelImmediately(Guid.NewGuid(), null, null, Now.AddDays(2));

        trial.Status.Should().Be(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public void EndTrial_on_an_active_subscription_is_rejected() =>
        FluentActions.Invoking(() => SubscriptionFactory.Active().EndTrial(PricingPolicy.RenewalCharge(100m, 0m, Tax, "SAR"), 3, Now))
            .Should().Throw<InvalidSubscriptionTransitionException>();

    [Fact]
    public void Trial_reminder_is_marked_once()
    {
        var trial = Trial();

        trial.MarkTrialReminderSent(Now.AddDays(11));

        trial.TrialReminderSentAt.Should().Be(Now.AddDays(11));
    }

    [Fact]
    public void A_once_coupon_discounts_exactly_one_renewal_then_disappears()
    {
        var sub = SubscriptionFactory.Active();
        sub.RedeemCoupon(Coupon(periods: 1), Now);

        var first = sub.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, Tax, "SAR", sub.ActiveCoupon), 7, sub.RenewalDate!.Value);
        first.Amount.Should().Be(90m);
        first.CouponCode.Should().Be("SAVE10");
        first.CouponDiscountAmount.Should().Be(10m);
        sub.ActiveCoupon.Should().BeNull();

        sub.Renew(sub.RenewalDate!.Value.AddDays(1));
        var second = sub.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, Tax, "SAR", sub.ActiveCoupon), 7, sub.RenewalDate!.Value);
        second.Amount.Should().Be(100m);
        second.CouponCode.Should().BeNull();
    }

    [Fact]
    public void A_repeating_coupon_counts_down_and_a_forever_coupon_never_ends()
    {
        var repeating = SubscriptionFactory.Active();
        repeating.RedeemCoupon(Coupon(periods: 2), Now);
        repeating.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, Tax, "SAR", repeating.ActiveCoupon), 7, repeating.RenewalDate!.Value);
        repeating.CouponPeriodsRemaining.Should().Be(1);

        var forever = SubscriptionFactory.Active();
        forever.RedeemCoupon(Coupon(periods: null), Now);
        forever.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, Tax, "SAR", forever.ActiveCoupon), 7, forever.RenewalDate!.Value);
        forever.ActiveCoupon.Should().NotBeNull();
    }

    [Fact]
    public void A_coupon_redeemed_with_the_subscription_discounts_the_activation_bill_and_counts_it()
    {
        var terms = Coupon(periods: 2);
        var (amountDue, discount) = PricingPolicy.ActivationCharge(100m, Tax, "SAR", terms);

        var sub = Subscription.Create(Guid.NewGuid(), SubscriptionFactory.Plan(100m), amountDue, Now, terms, discount);

        sub.ActivationPayment.Amount.Should().Be(90m);
        sub.ActivationPayment.CouponCode.Should().Be("SAVE10");
        sub.CouponPeriodsRemaining.Should().Be(1, "the activation bill used one of two");
    }

    [Fact]
    public void A_second_coupon_is_rejected_and_a_pending_subscription_cannot_redeem()
    {
        var sub = SubscriptionFactory.Active();
        sub.RedeemCoupon(Coupon(1), Now);

        FluentActions.Invoking(() => sub.RedeemCoupon(Coupon(1), Now)).Should().Throw<ConflictException>();
        FluentActions.Invoking(() => SubscriptionFactory.Pending().RedeemCoupon(Coupon(1), Now)).Should().Throw<ConflictException>();
    }

    [Fact]
    public void Trial_coupon_discounts_the_first_paid_bill()
    {
        var trial = Trial();
        trial.RedeemCoupon(Coupon(periods: 1, percent: 50m), Now);

        var payment = trial.EndTrial(
            PricingPolicy.RenewalCharge(100m, 0m, Tax, "SAR", trial.ActiveCoupon), 3, trial.TrialEndsAt!.Value);

        payment.Amount.Should().Be(50m);
    }
}
