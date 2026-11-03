using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Localization;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Subscriptions.Models;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.IntegrationTests;

[Collection(SubscriptionsPostgresCollection.Name)]
public sealed class InvoiceTests(SubscriptionsPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    [Fact]
    public async Task Approval_inserts_the_invoice_with_buyer_plan_and_tax_snapshot_and_delivery_runs_afterwards()
    {
        var plan = await fx.CreatePlanAsync("Snapshot-I1", 1150m);
        var subscriberId = Guid.NewGuid();
        fx.Subscribers.People[subscriberId] = new Contracts.Subscribers.SubscriberInfo(
            subscriberId, "Jane", "jane@example.com", BillingName: "Jane Ltd", BillingAddress: "1 Road", TaxId: "TAX-9");
        var created = await _scenario.CreateAsync(subscriberId, plan);
        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);

        var approval = await fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin));

        var invoice = await fx.WithDb(db => db.Invoices.AsNoTracking().SingleAsync(x => x.Id == approval.InvoiceId));
        invoice.Status.Should().Be(InvoiceStatus.Pending, "PDF + email run in the background job");
        invoice.BuyerName.Should().Be("Jane Ltd");
        invoice.BuyerTaxId.Should().Be("TAX-9");
        invoice.SellerName.Should().Be("Test Seller");
        invoice.SellerTaxId.Should().Be("VAT-123");
        invoice.PlanName.Should().Be("Snapshot-I1");
        invoice.NetAmount.Should().Be(1000m);
        invoice.TaxAmount.Should().Be(150m);
        invoice.TotalAmount.Should().Be(1150m);
        invoice.TaxRate.Should().Be(0.15m);
        invoice.PricesIncludeTax.Should().BeTrue();

        // Audited with amount + before/after.
        var audit = await fx.WithDb(db => db.AdminAuditLogs.AsNoTracking()
            .SingleAsync(x => x.Action == "ApproveSubscription" && x.TargetId == created.SubscriptionId));
        audit.Amount.Should().Be(1150m);
        audit.Currency.Should().Be("SAR");
        audit.AfterJson.Should().Contain(invoice.InvoiceNumber);

        // Rename the plan, then deliver: the PDF model comes from the snapshot, never from the live plan.
        var live = await fx.WithDb(db => db.Plans.AsNoTracking().SingleAsync(x => x.Id == plan.Id));
        await fx.SendAsAdmin(new UpdatePlanCommand(
            plan.Id, LocalizedText.Create("Renamed-I1", "Renamed-I1"), null, "SAR", 1, 10, 5, 3, true, true, live.Version));

        fx.Notifications.Messages.Clear();
        await fx.RunInvoiceDeliveriesAsync();

        var delivered = await fx.WithDb(db => db.Invoices.AsNoTracking().SingleAsync(x => x.Id == approval.InvoiceId));
        delivered.Status.Should().Be(InvoiceStatus.Sent);
        delivered.PdfStorageKey.Should().NotBeNull();
        delivered.PlanName.Should().Be("Snapshot-I1");
        delivered.Description.Should().Contain("Snapshot-I1").And.NotContain("Renamed-I1");

        var model = InvoicePdfModel.FromSnapshot(delivered, new InvoiceBrandingOptions { CompanyName = "Changed Later" });
        model.Description.Should().Contain("Snapshot-I1").And.NotContain("Renamed-I1");
        model.Seller.CompanyName.Should().Be("Test Seller");
        model.SubscriberName.Should().Be("Jane Ltd");
        model.TaxRateLabel.Should().Be("15%");

        var email = fx.Notifications.Messages.Single(x => x.UserId == subscriberId);
        email.Email!.Attachments.Should().ContainSingle(x => x.FileName == $"{delivered.InvoiceNumber}.pdf");

        // Idempotent: delivering again does not send a second email.
        fx.Jobs.Create(Hangfire.Common.Job.FromExpression<Domains.Subscriptions.Jobs.InvoiceDeliveryJob>(
            j => j.DeliverAsync(delivered.Id, false)), new Hangfire.States.EnqueuedState());
        await fx.RunInvoiceDeliveriesAsync();
        fx.Notifications.Messages.Count(x => x.UserId == subscriberId).Should().Be(1);
    }

    [Fact]
    public async Task Credit_notes_are_limited_to_the_creditable_remainder_and_numbered_from_the_same_sequence()
    {
        var plan = await fx.CreatePlanAsync("Credit-I2", 1150m);
        var (_, _, approval) = await _scenario.ActiveAsync(plan);

        var original = await fx.WithDb(db => db.Invoices.AsNoTracking().SingleAsync(x => x.Id == approval.InvoiceId));
        var partial = await fx.SendAsAdmin(new IssueCreditNoteCommand(original.Id, TestIds.Admin, "goodwill", 115m, original.Version));

        partial.TotalAmount.Should().Be(-115m);
        partial.CreditNoteNumber.Should().MatchRegex(@"^INV-\d{4}-\d{6}$");

        var stale = () => fx.SendAsAdmin(new IssueCreditNoteCommand(original.Id, TestIds.Admin, "stale", 1m, original.Version));
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();

        var tooMuch = () => fx.SendAsAdmin(new IssueCreditNoteCommand(original.Id, TestIds.Admin, "too much", 1035.01m));
        await tooMuch.Should().ThrowAsync<BadRequestException>();

        var rest = await fx.SendAsAdmin(new IssueCreditNoteCommand(original.Id, TestIds.Admin, "close"));
        rest.TotalAmount.Should().Be(-1035m);

        var notes = await fx.WithDb(db => db.Invoices.AsNoTracking()
            .Where(x => x.OriginalInvoiceId == original.Id).ToListAsync());
        notes.Sum(x => x.NetAmount).Should().Be(-original.NetAmount);
        notes.Sum(x => x.TaxAmount).Should().Be(-original.TaxAmount);

        var again = () => fx.SendAsAdmin(new IssueCreditNoteCommand(original.Id, TestIds.Admin, "again"));
        await again.Should().ThrowAsync<ConflictException>();

        var creditTheNote = () => fx.SendAsAdmin(new IssueCreditNoteCommand(partial.CreditNoteId, TestIds.Admin, "nope"));
        await creditTheNote.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Approving_without_payment_id_is_ambiguous_when_several_payments_are_submitted()
    {
        var plan = await fx.CreatePlanAsync("Ambiguous-I3", 100m);
        var subscriberId = Guid.NewGuid();
        var created = await _scenario.CreateAsync(subscriberId, plan);
        await _scenario.UploadActivationProofAsync(subscriberId, created.SubscriptionId, created.PaymentId);
        await fx.WithDb(async db =>
        {
            var extra = SubscriptionPayment.Create(created.SubscriptionId, PaymentPurpose.Activation, 100m, "SAR", DateTimeOffset.UtcNow);
            extra.SubmitProof(Guid.NewGuid(), new PaymentMethodSnapshot(null, "Bank", "Bank", "1", null), subscriberId, DateTimeOffset.UtcNow);
            db.Payments.Add(extra);
            return await db.SaveChangesAsync();
        });

        var withoutId = () => fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin));
        await withoutId.Should().ThrowAsync<BadRequestException>();

        var withId = await fx.SendAsAdmin(new ApproveSubscriptionCommand(created.SubscriptionId, TestIds.Admin, created.PaymentId));
        withId.PaymentId.Should().Be(created.PaymentId);
    }
}
