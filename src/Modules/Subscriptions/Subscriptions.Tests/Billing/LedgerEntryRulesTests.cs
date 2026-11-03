using FluentAssertions;
using Subscriptions.Contracts.Ledger.Enums;
using Subscriptions.Domains.Ledger.Models;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests.Billing;

/// <summary>Sign and snapshot rules of the immutable ledger (one factory per money event).</summary>
public class LedgerEntryRulesTests
{
    private static readonly DateTimeOffset Now = SubscriptionFactory.DefaultNow;
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public void Payment_entry_is_positive_and_snapshots_plan_method_and_links()
    {
        var subscription = SubscriptionFactory.Pending(1000m);
        var payment = subscription.ActivationPayment;
        var invoiceId = Guid.NewGuid();

        var entry = LedgerEntry.ForPayment(subscription, payment, 1000m, invoiceId, Admin, Now, "Cash", "RCPT-9");

        entry.Should().BeEquivalentTo(new
        {
            Type = LedgerEntryType.Payment,
            Amount = 1000m,
            Currency = "SAR",
            SubscriptionId = subscription.Id,
            subscription.SubscriberId,
            PaymentId = payment.Id,
            InvoiceId = invoiceId,
            ActorId = Admin,
            OccurredAt = Now,
            subscription.PlanId,
            subscription.PlanName,
            PaymentMethod = "Cash",
            Reference = "RCPT-9"
        });
        entry.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Payment_entry_defaults_its_method_to_the_payment_method_type()
    {
        var subscription = SubscriptionFactory.Pending();
        var payment = subscription.ActivationPayment;
        payment.SubmitProof(Guid.NewGuid(), new PaymentMethodSnapshot(Guid.NewGuid(), "BankTransfer", "Bank", "ACC", null), subscription.SubscriberId, Now);

        LedgerEntry.ForPayment(subscription, payment, 100m, null, Admin, Now).PaymentMethod.Should().Be("BankTransfer");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Payment_entry_must_be_positive(decimal amount)
    {
        var subscription = SubscriptionFactory.Pending();

        var act = () => LedgerEntry.ForPayment(subscription, subscription.ActivationPayment, amount, null, Admin, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Payment_entry_rejects_a_payment_of_another_subscription()
    {
        var subscription = SubscriptionFactory.Pending();
        var other = SubscriptionFactory.Pending();

        var act = () => LedgerEntry.ForPayment(subscription, other.ActivationPayment, 10m, null, Admin, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Refund_entry_is_the_negated_refund_with_its_reference()
    {
        var refund = SubscriptionRefundRequest.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Pro", 12, Now.AddMonths(-1), Now, Now.AddMonths(11),
            460m, "SAR", RefundType.Prorated);
        refund.Approve(Admin, null);
        refund.MarkProcessed(Admin, "TX-77");

        var entry = LedgerEntry.ForRefund(refund, null, Admin, Now);

        entry.Type.Should().Be(LedgerEntryType.Refund);
        entry.Amount.Should().Be(-460m);
        entry.RefundId.Should().Be(refund.Id);
        entry.Reference.Should().Be("TX-77");
        entry.PlanName.Should().Be("Pro");
    }

    [Fact]
    public void Credit_note_entry_carries_the_negative_credit_note_total()
    {
        var invoice = Invoice(1150m);
        var note = SubscriptionInvoice.IssueCreditNote(
            invoice, CreditNotePolicy.Compute(invoice.Amounts, new TaxBreakdown(0, 0, 0, 0.15m, true), 115m),
            "INV-2026-000002", "goodwill", Now);

        var entry = LedgerEntry.ForCreditNote(note, Admin, Now);

        entry.Type.Should().Be(LedgerEntryType.CreditNote);
        entry.Amount.Should().Be(-115m);
        entry.InvoiceId.Should().Be(note.Id);
        entry.Reference.Should().Be("INV-2026-000002");
    }

    [Fact]
    public void Credit_note_entry_refuses_a_regular_invoice()
    {
        var act = () => LedgerEntry.ForCreditNote(Invoice(100m), Admin, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Adjustment_may_be_zero_but_needs_a_description()
    {
        var subscription = SubscriptionFactory.Active();

        var comp = LedgerEntry.Adjustment(subscription, 0m, "Complimentary activation", Admin, Now);
        comp.Type.Should().Be(LedgerEntryType.Adjustment);
        comp.Amount.Should().Be(0m);

        LedgerEntry.Adjustment(subscription, -12.345m, "write-off", Admin, Now).Amount.Should().Be(-12.35m);

        var noReason = () => LedgerEntry.Adjustment(subscription, 0m, " ", Admin, Now);
        noReason.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ledger_entries_expose_no_mutators()
    {
        typeof(LedgerEntry).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Should().BeEmpty("the ledger is append-only");

        typeof(LedgerEntry).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(LedgerEntry) && !m.IsSpecialName)
            .Should().BeEmpty("no instance method may change an entry");
    }

    private static SubscriptionInvoice Invoice(decimal gross) =>
        SubscriptionInvoice.Issue(
            new InvoiceDraft(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), InvoiceType.Activation, "Activation - Pro",
                Guid.NewGuid(), "Pro", 1, Now, Now.AddMonths(1),
                new InvoiceParty("Buyer", null, null, null),
                new InvoiceParty("Seller", null, null, null),
                TaxCalculator.FromGross(gross, SubscriptionFactory.Tax), "SAR"),
            "INV-2026-000001",
            Now);
}
