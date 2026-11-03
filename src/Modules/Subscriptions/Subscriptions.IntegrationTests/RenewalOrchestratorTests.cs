using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Models;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.IntegrationTests;

[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class RenewalOrchestratorTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    private IRenewalOrchestrator Orchestrator => fx.Services.GetRequiredService<IRenewalOrchestrator>();

    [Fact]
    public async Task End_of_period_cancellation_wins_over_past_due_and_nothing_is_billed()
    {
        var plan = await fx.CreatePlanAsync("Cancel-R1", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAs(subscriberId, new CancelSubscriptionCommand(subscriptionId, subscriberId, false, "bye"));
        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;

        var outcome = await Orchestrator.ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));

        outcome.Should().Be(RenewalBoundaryOutcome.Cancelled);
        var subscription = await _scenario.LoadAsync(subscriptionId);
        subscription.Status.Should().Be(SubscriptionStatus.Cancelled);
        subscription.EndDate.Should().BeCloseTo(renewal, TimeSpan.FromSeconds(1));
        (await _scenario.PaymentsAsync(subscriptionId)).Should().NotContain(x => x.Purpose == PaymentPurpose.Renewal);
    }

    [Fact]
    public async Task Downgrade_is_applied_before_the_renewal_payment_is_priced_and_the_cycle_stays_anchored()
    {
        var pro = await fx.CreatePlanAsync("Pro-R2", 300m);
        var basic = await fx.CreatePlanAsync("Basic-R2", 120m, features: "basic-only");
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(pro);
        await fx.SendAs(subscriberId, new ScheduleDowngradeCommand(subscriptionId, subscriberId, basic.Id));
        var active = await _scenario.LoadAsync(subscriptionId);
        var renewal = active.RenewalDate!.Value;

        var outcome = await Orchestrator.ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));

        outcome.Should().Be(RenewalBoundaryOutcome.BilledAfterDowngrade);
        var pastDue = await _scenario.LoadAsync(subscriptionId);
        pastDue.Status.Should().Be(SubscriptionStatus.PastDue);
        pastDue.PlanId.Should().Be(basic.Id);
        pastDue.FeaturesJson.Should().Contain("basic-only");
        var renewalPayment = (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Purpose == PaymentPurpose.Renewal);
        renewalPayment.Amount.Should().Be(120m, "priced on the downgraded plan, not the old one");

        // Approve the renewal: amount comes from the payment, the new period starts at the old renewal date.
        await _scenario.UploadRenewalProofAsync(subscriberId, subscriptionId);
        var approval = await fx.SendAsAdmin(new ApproveRenewalCommand(subscriptionId, TestIds.Admin, renewalPayment.Id, pastDue.Version));

        var renewed = await _scenario.LoadAsync(subscriptionId);
        renewed.Status.Should().Be(SubscriptionStatus.Active);
        renewed.CurrentPeriodStart.Should().BeCloseTo(renewal, TimeSpan.FromSeconds(1));
        approval.NewRenewalDate.Should().BeCloseTo(active.StartDate!.Value.AddMonths(2), TimeSpan.FromSeconds(1));
        var invoice = (await _scenario.InvoicesAsync(subscriptionId)).Single(x => x.Id == approval.InvoiceId);
        invoice.Type.Should().Be(InvoiceType.Renewal);
        invoice.TotalAmount.Should().Be(120m);
        invoice.PlanName.Should().Be("Basic-R2");
    }

    [Fact]
    public async Task Scheduled_price_change_is_billed_at_the_boundary()
    {
        var plan = await fx.CreatePlanAsync("PriceUp-R3", 100m);
        var (_, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        var renewal = (await _scenario.LoadAsync(subscriptionId)).RenewalDate!.Value;
        await fx.WithDb(async db =>
        {
            var tracked = await db.Plans.SingleAsync(x => x.Id == plan.Id);
            tracked.SchedulePriceChange(150m, renewal.AddMinutes(-1));
            return await db.SaveChangesAsync();
        });

        await Orchestrator.ProcessBoundaryAsync(subscriptionId, renewal.AddHours(1));

        var payment = (await _scenario.PaymentsAsync(subscriptionId)).Single(x => x.Purpose == PaymentPurpose.Renewal);
        payment.Amount.Should().Be(150m);
        (await _scenario.LoadAsync(subscriptionId)).Price.Should().Be(150m);
    }

    [Fact]
    public async Task Expiry_skips_subscriptions_whose_renewal_proof_is_under_review()
    {
        var plan = await fx.CreatePlanAsync("Expiry-R4", 100m);
        var (waitingSubscriber, waiting, _) = await _scenario.ActiveAsync(plan);
        var (_, silent, _) = await _scenario.ActiveAsync(plan);

        foreach (var id in new[] { waiting, silent })
        {
            var renewal = (await _scenario.LoadAsync(id)).RenewalDate!.Value;
            await Orchestrator.ProcessBoundaryAsync(id, renewal.AddHours(1));
        }

        await _scenario.UploadRenewalProofAsync(waitingSubscriber, waiting);
        var graceEnd = (await _scenario.LoadAsync(silent)).GracePeriodEndsAt!.Value;

        await Orchestrator.ProcessExpiriesAsync(graceEnd.AddDays(1));

        (await _scenario.LoadAsync(waiting)).Status.Should().Be(SubscriptionStatus.PastDue);
        (await _scenario.LoadAsync(silent)).Status.Should().Be(SubscriptionStatus.Expired);
        (await _scenario.PaymentsAsync(silent)).Single(x => x.Purpose == PaymentPurpose.Renewal)
            .Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task A_second_downgrade_can_be_scheduled_after_the_first_was_cancelled()
    {
        var pro = await fx.CreatePlanAsync("Pro-R5", 300m);
        var low = await fx.CreatePlanAsync("Low-R5", 100m);
        var lower = await fx.CreatePlanAsync("Lower-R5", 50m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(pro);

        await fx.SendAs(subscriberId, new ScheduleDowngradeCommand(subscriptionId, subscriberId, low.Id));
        await fx.SendAs(subscriberId, new CancelScheduledDowngradeCommand(subscriptionId, subscriberId));
        var second = await fx.SendAs(subscriberId, new ScheduleDowngradeCommand(subscriptionId, subscriberId, lower.Id));

        second.NewPlanName.Should().Be("Lower-R5");
        var changes = (await _scenario.LoadAsync(subscriptionId)).ScheduledPlanChanges;
        changes.Should().HaveCount(2);
        changes.Count(x => x.Status == PlanChangeStatus.Pending).Should().Be(1);

        var third = () => fx.SendAs(subscriberId, new ScheduleDowngradeCommand(subscriptionId, subscriberId, low.Id));
        await third.Should().ThrowAsync<Shared.Core.Exceptions.ConflictException>();
    }
}
