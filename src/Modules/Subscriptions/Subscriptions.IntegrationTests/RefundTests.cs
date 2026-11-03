using Microsoft.EntityFrameworkCore;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.IntegrationTests;

[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class RefundTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    [Fact]
    public async Task Refund_is_the_money_received_and_a_second_refund_is_impossible()
    {
        var plan = await fx.CreatePlanAsync("Annual", 1150m, cycle: 12);
        var (subscriberId, subscriptionId, approval) = await _scenario.ActiveAsync(plan);

        await fx.SendAsAdmin(new CancelSubscriptionCommand(subscriptionId, TestIds.Admin, true, "test", CancellationReason.Other));

        var refundId = await fx.SendAs(subscriberId, new RequestRefundCommand(subscriptionId, subscriberId));
        var refund = await fx.WithDb(db => db.RefundRequests.AsNoTracking().SingleAsync(x => x.Id == refundId));
        refund.RequestedAmount.Should().Be(1150m, "full window: everything that was paid, never more");
        refund.RefundType.Should().Be(RefundType.Full);

        var duplicate = () => fx.SendAs(subscriberId, new RequestRefundCommand(subscriptionId, subscriberId));
        await duplicate.Should().ThrowAsync<ConflictException>();

        await fx.SendAsAdmin(new AdminReviewRefundCommand(refundId, TestIds.Admin, true, "ok", refund.Version));
        await fx.SendAsAdmin(new MarkRefundProcessedCommand(refundId, TestIds.Admin, "BANK-REF-1"));

        // Processed refunds produce a credit note against the activation invoice.
        var invoices = await _scenario.InvoicesAsync(subscriptionId);
        var activation = invoices.Single(x => x.Id == approval.InvoiceId);
        var note = invoices.Single(x => x.Type == InvoiceType.CreditNote);
        note.OriginalInvoiceId.Should().Be(activation.Id);
        note.TotalAmount.Should().Be(-1150m);
        (note.NetAmount + note.TaxAmount).Should().Be(note.TotalAmount);
        activation.CreditedAmount.Should().Be(1150m);

        var again = () => fx.SendAs(subscriberId, new RequestRefundCommand(subscriptionId, subscriberId));
        await again.Should().ThrowAsync<ConflictException>("a processed refund blocks any further refund");

        // The database itself refuses a second non-rejected refund (Processed is in the filtered unique index).
        var bypass = () => fx.WithDb(async db =>
        {
            db.RefundRequests.Add(SubscriptionRefundRequest.Create(
                subscriptionId, subscriberId, plan.Id, "Annual", 12, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddYears(1), 1150m, "SAR", RefundType.Full));
            return await db.SaveChangesAsync();
        });
        (await bypass.Should().ThrowAsync<ConflictException>()).WithMessage("*refund has already been*");
    }

    [Fact]
    public async Task Approval_re_checks_the_refundable_cap()
    {
        var plan = await fx.CreatePlanAsync("AnnualCap", 1000m, cycle: 12);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAsAdmin(new CancelSubscriptionCommand(subscriptionId, TestIds.Admin, true, null));
        var refundId = await fx.SendAs(subscriberId, new RequestRefundCommand(subscriptionId, subscriberId));

        // Simulate money already refunded out of band: the received payment shrinks below the request.
        await fx.WithDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE subscriptions.subscription_payments SET amount = 400 WHERE subscription_id = {subscriptionId}"));

        var approve = () => fx.SendAsAdmin(new AdminReviewRefundCommand(refundId, TestIds.Admin, true, null));

        (await approve.Should().ThrowAsync<ConflictException>()).WithMessage("*exceeds the refundable amount*");
    }

    [Fact]
    public async Task Monthly_plans_are_not_refundable()
    {
        var plan = await fx.CreatePlanAsync("MonthlyNoRefund", 100m);
        var (subscriberId, subscriptionId, _) = await _scenario.ActiveAsync(plan);
        await fx.SendAsAdmin(new CancelSubscriptionCommand(subscriptionId, TestIds.Admin, true, null));

        var act = () => fx.SendAs(subscriberId, new RequestRefundCommand(subscriptionId, subscriberId));

        await act.Should().ThrowAsync<ConflictException>();
    }
}
