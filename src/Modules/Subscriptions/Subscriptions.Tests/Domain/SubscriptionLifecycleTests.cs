using FluentAssertions;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Exceptions;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests.Domain;

public class SubscriptionLifecycleTests
{
    private static readonly DateTimeOffset Start = new(2025, 1, 31, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Activation_sets_period_start_and_month_end_clamped_renewal()
    {
        var subscription = SubscriptionFactory.Active(now: Start);

        subscription.StartDate.Should().Be(Start);
        subscription.CurrentPeriodStart.Should().Be(Start);
        subscription.RenewalDate.Should().Be(new DateTimeOffset(2025, 2, 28, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Late_renewal_anchors_on_the_previous_renewal_date_not_now()
    {
        var subscription = SubscriptionFactory.Active(now: Start);
        var firstRenewal = subscription.RenewalDate!.Value;
        subscription.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, SubscriptionFactory.Tax), 7, firstRenewal);

        var approvedLate = firstRenewal.AddDays(5);
        subscription.Renew(approvedLate);

        subscription.CurrentPeriodStart.Should().Be(firstRenewal);
        subscription.RenewalDate.Should().Be(new DateTimeOffset(2025, 3, 31, 9, 0, 0, TimeSpan.Zero));
        subscription.GracePeriodEndsAt.Should().BeNull();
    }

    [Fact]
    public void Past_due_bills_the_period_price_plus_deferred_proration_and_clears_it()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        var upgrade = SubscriptionFactory.Plan(150m);
        subscription.UpgradePlan(upgrade, proratedAmount: 12.5m, isDeferred: true, amountDue: 0m, Start.AddDays(10))
            .Should().BeNull();
        subscription.DeferredProrationAmount.Should().Be(12.5m);

        var charge = PricingPolicy.RenewalCharge(subscription.Price, subscription.DeferredProrationAmount, SubscriptionFactory.Tax);
        var payment = subscription.MarkPastDue(charge, 7, subscription.RenewalDate!.Value);

        payment.Purpose.Should().Be(PaymentPurpose.Renewal);
        payment.Amount.Should().Be(162.5m);
        payment.IncludedProrationAmount.Should().Be(12.5m);
        subscription.DeferredProrationAmount.Should().Be(0m);
        subscription.Price.Should().Be(150m);
    }

    [Fact]
    public void Non_deferred_upgrade_creates_a_proration_payment_remembering_the_original_plan()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        var originalPlanId = subscription.PlanId;

        var payment = subscription.UpgradePlan(SubscriptionFactory.Plan(200m), 50m, false, 57.5m, Start.AddDays(3));

        payment.Should().NotBeNull();
        payment!.Purpose.Should().Be(PaymentPurpose.Proration);
        payment.Amount.Should().Be(57.5m);
        payment.OriginalPlanId.Should().Be(originalPlanId);
        subscription.Price.Should().Be(200m);
    }

    [Fact]
    public void A_second_upgrade_is_refused_while_the_first_is_unpaid()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        subscription.UpgradePlan(SubscriptionFactory.Plan(200m), 50m, false, 50m, Start.AddDays(1));

        var act = () => subscription.UpgradePlan(SubscriptionFactory.Plan(300m), 50m, false, 50m, Start.AddDays(2));

        act.Should().Throw<ConflictException>().WithMessage("*awaiting payment*");
    }

    [Theory]
    [InlineData("USD", 1)]
    [InlineData("SAR", 12)]
    public void Plan_changes_across_currency_or_cycle_are_rejected(string currency, int cycle)
    {
        var subscription = SubscriptionFactory.Active(price: 100m, currency: "SAR", cycle: 1, now: Start);

        var upgrade = () => subscription.UpgradePlan(SubscriptionFactory.Plan(200m, currency, cycle), 10m, false, 10m, Start);
        var downgrade = () => subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m, currency, cycle), Guid.NewGuid(), Start);

        upgrade.Should().Throw<PlanChangeNotSupportedException>();
        downgrade.Should().Throw<PlanChangeNotSupportedException>();
    }

    [Fact]
    public void Upgrading_cancels_a_pending_scheduled_downgrade()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        var downgrade = subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m), Guid.NewGuid(), Start.AddDays(1));

        subscription.UpgradePlan(SubscriptionFactory.Plan(200m), 10m, true, 0m, Start.AddDays(2));

        downgrade.Status.Should().Be(PlanChangeStatus.Cancelled);
        subscription.PendingPlanChange.Should().BeNull();
    }

    [Fact]
    public void A_second_downgrade_can_be_scheduled_after_the_first_is_cancelled()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        var first = subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m), Guid.NewGuid(), Start.AddDays(1));
        subscription.CancelScheduledDowngrade(Start.AddDays(2));

        var second = subscription.ScheduleDowngrade(SubscriptionFactory.Plan(60m), Guid.NewGuid(), Start.AddDays(3));

        first.Status.Should().Be(PlanChangeStatus.Cancelled);
        second.Status.Should().Be(PlanChangeStatus.Pending);
        subscription.ScheduledPlanChanges.Should().HaveCount(2);
        subscription.PendingPlanChange.Should().BeSameAs(second);
    }

    [Fact]
    public void Scheduled_downgrade_applies_only_when_active_and_due()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m, name: "Basic"), Guid.NewGuid(), Start);

        var early = () => subscription.ApplyScheduledDowngrade(Start.AddDays(1));
        early.Should().Throw<ConflictException>();

        subscription.ApplyScheduledDowngrade(subscription.RenewalDate!.Value);
        subscription.PlanName.Should().Be("Basic");
        subscription.Price.Should().Be(50m);
        subscription.PendingPlanChange.Should().BeNull();
    }

    [Fact]
    public void Scheduled_downgrade_is_not_applied_to_a_past_due_subscription()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m), Guid.NewGuid(), Start);
        subscription.MarkPastDue(PricingPolicy.RenewalCharge(100m, 0m, SubscriptionFactory.Tax), 7, subscription.RenewalDate!.Value);

        var act = () => subscription.ApplyScheduledDowngrade(subscription.RenewalDate!.Value.AddDays(1));

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Scheduled_cancellation_ends_at_the_period_boundary_and_cancels_the_pending_downgrade()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        var downgrade = subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m), Guid.NewGuid(), Start.AddDays(1));
        subscription.ScheduleCancellation(subscription.SubscriberId, CancellationReason.PriceTooHigh, null, Start.AddDays(2));

        var boundary = subscription.RenewalDate!.Value;
        subscription.ApplyScheduledCancellation(Guid.NewGuid(), boundary.AddHours(1));

        subscription.Status.Should().Be(SubscriptionStatus.Cancelled);
        subscription.EndDate.Should().Be(boundary);
        downgrade.Status.Should().Be(PlanChangeStatus.Cancelled);
    }

    [Fact]
    public void Cannot_schedule_a_downgrade_once_cancellation_is_scheduled()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, now: Start);
        subscription.ScheduleCancellation(subscription.SubscriberId, null, null, Start.AddDays(1));

        var act = () => subscription.ScheduleDowngrade(SubscriptionFactory.Plan(50m), Guid.NewGuid(), Start.AddDays(2));

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Payment_approval_requires_a_submitted_proof()
    {
        var subscription = SubscriptionFactory.Pending();
        var payment = subscription.ActivationPayment;

        var act = () => payment.Approve(Guid.NewGuid(), subscription.SubscriberId, Start);

        act.Should().Throw<ConflictException>();
        payment.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void Rejected_payment_can_be_resubmitted_and_mark_failed_closes_it()
    {
        var payment = SubscriptionPayment.Create(Guid.NewGuid(), PaymentPurpose.Proration, 10m, "SAR", Start);
        var method = new PaymentMethodSnapshot(Guid.NewGuid(), "Bank", "Bank", "123", null);
        payment.SubmitProof(Guid.NewGuid(), method, Guid.NewGuid(), Start);
        payment.Reject(Guid.NewGuid(), "blurry", Guid.NewGuid(), Start);

        payment.SubmitProof(Guid.NewGuid(), method, Guid.NewGuid(), Start.AddHours(1));
        payment.Status.Should().Be(PaymentStatus.Submitted);

        payment.MarkFailed("closed");
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.IsAwaitingPayment.Should().BeFalse();
    }

    [Fact]
    public void Issued_invoice_is_immutable_and_credit_notes_track_the_credited_amount()
    {
        var draft = new InvoiceDraft(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InvoiceType.Activation, "Activation - Pro",
            Guid.NewGuid(), "Pro", 1, Start, Start.AddMonths(1),
            new InvoiceParty("Buyer", "b@example.com", null, null),
            new InvoiceParty("Seller", null, null, "VAT-1"),
            TaxCalculator.FromGross(1150m, SubscriptionFactory.Tax), "SAR");
        var invoice = SubscriptionInvoice.Issue(draft, "INV-2026-000001", Start);

        var partial = SubscriptionInvoice.IssueCreditNote(
            invoice, CreditNotePolicy.Compute(invoice.Amounts, new TaxBreakdown(0, 0, 0, 0.15m, true), 115m),
            "INV-2026-000002", "goodwill", Start);

        partial.Type.Should().Be(InvoiceType.CreditNote);
        partial.OriginalInvoiceId.Should().Be(invoice.Id);
        partial.TotalAmount.Should().Be(-115m);
        partial.BuyerName.Should().Be("Buyer");
        invoice.CreditedAmount.Should().Be(115m);
        invoice.TotalAmount.Should().Be(1150m);
        invoice.IsFullyCredited.Should().BeFalse();

        var tooMuch = () => SubscriptionInvoice.IssueCreditNote(
            invoice, new TaxBreakdown(-1000m, -150m, -1150m, 0.15m, true), "INV-2026-000003", "x", Start);
        tooMuch.Should().Throw<ConflictException>();

        var creditTheNote = () => SubscriptionInvoice.IssueCreditNote(
            partial, new TaxBreakdown(-1m, 0m, -1m, 0.15m, true), "INV-2026-000004", "x", Start);
        creditTheNote.Should().Throw<ConflictException>();
    }
}
