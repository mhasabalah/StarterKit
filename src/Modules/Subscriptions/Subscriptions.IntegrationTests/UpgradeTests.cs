using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Jobs;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.IntegrationTests;

[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class UpgradeTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    [Fact]
    public async Task Upgrade_then_proof_then_approval_pays_the_proration_and_keeps_its_invoice()
    {
        var basic = await fx.CreatePlanAsync("Basic-U1", 100m);
        var pro = await fx.CreatePlanAsync("Pro-U1", 200m, features: "reports");
        var (subscriberId, subscriptionId, activation) = await _scenario.ActiveAsync(basic);

        var upgrade = await fx.SendAs(subscriberId, new UpgradeSubscriptionCommand(subscriptionId, subscriberId, pro.Id));

        upgrade.IsDeferred.Should().BeFalse("a new subscriber is not trusted");
        upgrade.PaymentId.Should().NotBeNull();
        upgrade.InvoiceId.Should().NotBeNull();
        upgrade.ProratedAmount.Should().BeInRange(99m, 100m, "upgraded right after activation: ~the whole price difference");
        upgrade.AmountDue.Should().Be(upgrade.ProratedAmount, "prices include tax");
        upgrade.ProofRequiredBy.Should().NotBeNull();

        var subscription = await _scenario.LoadAsync(subscriptionId);
        subscription.PlanId.Should().Be(pro.Id, "upgrades are immediate");
        subscription.FeaturesJson.Should().Contain("reports");

        await _scenario.UploadProrationProofAsync(subscriberId, subscriptionId, upgrade.PaymentId!.Value);
        var pending = await fx.WithDb(db => db.Payments.AsNoTracking().SingleAsync(x => x.Id == upgrade.PaymentId));
        pending.Status.Should().Be(PaymentStatus.Submitted);

        var approved = await fx.SendAsAdmin(new ApproveProrationPaymentCommand(
            subscriptionId, upgrade.PaymentId.Value, TestIds.Admin, pending.Version));

        approved.PaymentStatus.Should().Be(PaymentStatus.Paid.ToString());
        approved.InvoiceId.Should().Be(upgrade.InvoiceId!.Value, "the proration invoice was issued with the upgrade");

        var invoices = await _scenario.InvoicesAsync(subscriptionId);
        invoices.Should().HaveCount(2);
        var proration = invoices.Single(x => x.Id == upgrade.InvoiceId);
        proration.Type.Should().Be(InvoiceType.Proration);
        proration.TotalAmount.Should().Be(upgrade.AmountDue);
        proration.PlanName.Should().Be("Pro-U1");
        invoices.Single(x => x.Id == activation.InvoiceId).CreditedAmount.Should().Be(0m);

        // Paid upgrades are never reverted.
        var outcomes = await RunSlaAsync(DateTimeOffset.UtcNow.AddHours(100));
        outcomes.Should().NotContainKey(upgrade.PaymentId.Value);
        (await _scenario.LoadAsync(subscriptionId)).PlanId.Should().Be(pro.Id);
    }

    [Fact]
    public async Task Sla_reverts_only_unpaid_upgrades_and_credits_only_their_invoice()
    {
        var basic = await fx.CreatePlanAsync("Basic-U2", 100m);
        var pro = await fx.CreatePlanAsync("Pro-U2", 300m);

        // A: unpaid (no proof) → reverted.
        var (subscriberA, subscriptionA, activationA) = await _scenario.ActiveAsync(basic);
        var upgradeA = await fx.SendAs(subscriberA, new UpgradeSubscriptionCommand(subscriptionA, subscriberA, pro.Id));

        // B: proof submitted, awaiting admin review → NOT reverted.
        var (subscriberB, subscriptionB, _) = await _scenario.ActiveAsync(basic);
        var upgradeB = await fx.SendAs(subscriberB, new UpgradeSubscriptionCommand(subscriptionB, subscriberB, pro.Id));
        await _scenario.UploadProrationProofAsync(subscriberB, subscriptionB, upgradeB.PaymentId!.Value);

        // C: proof rejected and not re-submitted → reverted.
        var (subscriberC, subscriptionC, _) = await _scenario.ActiveAsync(basic);
        var upgradeC = await fx.SendAs(subscriberC, new UpgradeSubscriptionCommand(subscriptionC, subscriberC, pro.Id));
        await _scenario.UploadProrationProofAsync(subscriberC, subscriptionC, upgradeC.PaymentId!.Value);
        await fx.SendAsAdmin(new RejectProrationPaymentCommand(subscriptionC, upgradeC.PaymentId.Value, TestIds.Admin, "unreadable"));

        // Before the SLA nothing happens.
        (await RunSlaAsync(DateTimeOffset.UtcNow.AddHours(71))).Should().NotContainKey(upgradeA.PaymentId!.Value);

        var outcomes = await RunSlaAsync(DateTimeOffset.UtcNow.AddHours(73));

        outcomes[upgradeA.PaymentId!.Value].Should().Be(UpgradeSlaOutcome.Reverted);
        outcomes[upgradeC.PaymentId.Value].Should().Be(UpgradeSlaOutcome.Reverted);
        outcomes.Should().NotContainKey(upgradeB.PaymentId.Value);

        var a = await _scenario.LoadAsync(subscriptionA);
        a.PlanId.Should().Be(basic.Id);
        a.Price.Should().Be(100m);
        a.Status.Should().Be(SubscriptionStatus.Active);
        (await _scenario.LoadAsync(subscriptionB)).PlanId.Should().Be(pro.Id);

        var paymentA = await fx.WithDb(db => db.Payments.AsNoTracking().SingleAsync(x => x.Id == upgradeA.PaymentId));
        paymentA.Status.Should().Be(PaymentStatus.Failed);

        var invoicesA = await _scenario.InvoicesAsync(subscriptionA);
        var note = invoicesA.Single(x => x.Type == InvoiceType.CreditNote);
        note.OriginalInvoiceId.Should().Be(upgradeA.InvoiceId);
        note.TotalAmount.Should().Be(-upgradeA.AmountDue);
        invoicesA.Single(x => x.Id == upgradeA.InvoiceId).CreditedAmount.Should().Be(upgradeA.AmountDue);
        invoicesA.Single(x => x.Id == activationA.InvoiceId).CreditedAmount.Should().Be(0m, "paid invoices are never credited");

        (await _scenario.InvoicesAsync(subscriptionB)).Should().NotContain(x => x.Type == InvoiceType.CreditNote);

        // Idempotent: a second run does nothing more.
        (await RunSlaAsync(DateTimeOffset.UtcNow.AddHours(80))).Should().NotContainKey(upgradeA.PaymentId.Value);
        (await _scenario.InvoicesAsync(subscriptionA)).Count(x => x.Type == InvoiceType.CreditNote).Should().Be(1);
    }

    [Fact]
    public async Task Upgrading_cancels_a_pending_downgrade_and_cross_currency_changes_are_rejected()
    {
        var mid = await fx.CreatePlanAsync("Mid-U3", 200m);
        var low = await fx.CreatePlanAsync("Low-U3", 100m);
        var high = await fx.CreatePlanAsync("High-U3", 400m);
        var usd = await fx.CreatePlanAsync("Usd-U3", 500m, currency: "USD");
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(mid);

        await fx.SendAs(subscriberId, new ScheduleDowngradeCommand(subscriptionId, subscriberId, low.Id));
        var crossCurrency = () => fx.SendAs(subscriberId, new UpgradeSubscriptionCommand(subscriptionId, subscriberId, usd.Id));
        await crossCurrency.Should().ThrowAsync<BadRequestException>();

        await fx.SendAs(subscriberId, new UpgradeSubscriptionCommand(subscriptionId, subscriberId, high.Id));

        var subscription = await _scenario.LoadAsync(subscriptionId);
        subscription.ScheduledPlanChanges.Should().ContainSingle()
            .Which.Status.Should().Be(PlanChangeStatus.Cancelled);

        var stacked = () => fx.SendAs(subscriberId, new UpgradeSubscriptionCommand(subscriptionId, subscriberId, high.Id));
        await stacked.Should().ThrowAsync<ConflictException>();
    }

    private async Task<IReadOnlyDictionary<Guid, UpgradeSlaOutcome>> RunSlaAsync(DateTimeOffset now) =>
        await ActivatorUtilities.CreateInstance<ProcessUpgradeSlaExpiryJob>(fx.Services).ProcessAsync(now, CancellationToken.None);
}
