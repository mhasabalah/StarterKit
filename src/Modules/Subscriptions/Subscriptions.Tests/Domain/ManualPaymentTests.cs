using FluentAssertions;
using Shared.Core.Exceptions;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests.Domain;

public class ManualPaymentTests
{
    private static readonly DateTimeOffset Now = SubscriptionFactory.DefaultNow;
    private static readonly Guid Admin = Guid.NewGuid();

    [Fact]
    public void Partial_receipts_accumulate_until_the_amount_due_is_reached()
    {
        var payment = Owed(1150m);

        var (first, firstCompleted) = Receive(payment, 400m);
        firstCompleted.Should().BeFalse();
        payment.Status.Should().Be(PaymentStatus.PartiallyPaid);
        payment.AmountReceived.Should().Be(400m);
        payment.OutstandingAmount.Should().Be(750m);
        payment.IsAwaitingPayment.Should().BeTrue("a partially paid payment is still owed");
        payment.PaidAt.Should().BeNull();

        var (_, secondCompleted) = Receive(payment, 500m);
        secondCompleted.Should().BeFalse();
        payment.OutstandingAmount.Should().Be(250m);

        var (last, completed) = Receive(payment, 250m);
        completed.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.AmountReceived.Should().Be(1150m);
        payment.OutstandingAmount.Should().Be(0m);
        payment.PaidAt.Should().Be(Now);
        payment.ReviewedBy.Should().Be(Admin);

        payment.Receipts.Should().HaveCount(3);
        payment.Receipts.Sum(x => x.Amount).Should().Be(payment.Amount);
        first.Id.Should().NotBe(last.Id);
    }

    [Fact]
    public void A_single_receipt_for_the_full_amount_completes_the_payment()
    {
        var payment = Owed(100m);

        var (_, completed) = Receive(payment, 100m);

        completed.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void Overpayment_is_rejected_by_default_and_nothing_is_recorded()
    {
        var payment = Owed(100m);
        Receive(payment, 60m);

        var act = () => Receive(payment, 40.01m);

        act.Should().Throw<BadRequestException>().WithMessage("*exceeds the outstanding 40.00*");
        payment.AmountReceived.Should().Be(60m);
        payment.Receipts.Should().HaveCount(1);
        payment.Status.Should().Be(PaymentStatus.PartiallyPaid);
    }

    [Fact]
    public void Overpayment_is_accepted_when_credit_is_allowed()
    {
        var payment = Owed(100m);

        var (_, completed) = payment.RecordReceipt(120m, "Cash", null, Now, null, Admin, Guid.NewGuid(), Now, allowOverpayment: true);

        completed.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.AmountReceived.Should().Be(120m, "the excess stays recorded as received money");
        payment.OutstandingAmount.Should().Be(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.001)]
    public void Amount_must_be_positive_with_two_decimals(decimal amount)
    {
        var act = () => Receive(Owed(100m), amount);

        act.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void A_paid_payment_takes_no_more_receipts()
    {
        var payment = Owed(100m);
        Receive(payment, 100m);

        var act = () => Receive(payment, 1m);

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Approving_a_proof_after_partial_receipts_receives_only_the_outstanding_amount()
    {
        var payment = Owed(1000m);
        Receive(payment, 300m);
        payment.SubmitProof(Guid.NewGuid(), Method(), Guid.NewGuid(), Now);

        var received = payment.Approve(Admin, Guid.NewGuid(), Now);

        received.Should().Be(700m);
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.AmountReceived.Should().Be(1000m);
    }

    [Fact]
    public void Approving_a_proof_without_receipts_receives_the_full_amount()
    {
        var payment = Owed(250m);
        payment.SubmitProof(Guid.NewGuid(), Method(), Guid.NewGuid(), Now);

        payment.Approve(Admin, Guid.NewGuid(), Now).Should().Be(250m);
    }

    [Fact]
    public void Rejecting_a_proof_of_a_partially_paid_payment_keeps_the_received_money()
    {
        var payment = Owed(1000m);
        Receive(payment, 300m);
        payment.SubmitProof(Guid.NewGuid(), Method(), Guid.NewGuid(), Now);

        payment.Reject(Admin, "unreadable", Guid.NewGuid(), Now);

        payment.Status.Should().Be(PaymentStatus.PartiallyPaid);
        payment.AmountReceived.Should().Be(300m);
        payment.AcceptsProof.Should().BeTrue();
    }

    [Fact]
    public void A_manual_receipt_is_accepted_while_a_proof_is_under_review()
    {
        var payment = Owed(500m);
        payment.SubmitProof(Guid.NewGuid(), Method(), Guid.NewGuid(), Now);

        var (_, completed) = Receive(payment, 500m);

        completed.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void A_manual_method_is_recorded_when_the_payment_had_none()
    {
        var payment = Owed(100m);

        Receive(payment, 10m, method: " Cash ");

        payment.PaymentMethodType.Should().Be(SubscriptionPayment.ManualMethod);
        payment.PaymentMethodDisplayName.Should().Be("Cash");
        payment.Receipts.Single().Method.Should().Be("Cash");
    }

    [Fact]
    public void Complimentary_payment_is_paid_with_zero_amount()
    {
        var payment = SubscriptionPayment.CreateComplimentary(Guid.NewGuid(), "SAR", Admin, Now);

        payment.Should().BeEquivalentTo(new
        {
            Amount = 0m,
            Status = PaymentStatus.Paid,
            Purpose = PaymentPurpose.Activation,
            PaymentMethodType = SubscriptionPayment.ComplimentaryMethod,
            IsAwaitingPayment = false
        });
    }

    private static SubscriptionPayment Owed(decimal amount) =>
        SubscriptionPayment.Create(Guid.NewGuid(), PaymentPurpose.Activation, amount, "SAR", Now);

    private static (SubscriptionPaymentReceipt Receipt, bool Completed) Receive(
        SubscriptionPayment payment,
        decimal amount,
        string method = "BankTransfer") =>
        payment.RecordReceipt(amount, method, "REF-1", Now, null, Admin, Guid.NewGuid(), Now);

    private static PaymentMethodSnapshot Method() => new(Guid.NewGuid(), "BankTransfer", "Bank", "ACC", null);
}
