using FluentAssertions;
using Subscriptions.Domains.PaymentSettings.Models;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests;

public class PaymentMethodSnapshotTests
{
    [Fact]
    public void SubmitProof_stores_payment_method_id_and_snapshot()
    {
        var paymentMethod = PaymentMethod.Create(
            "BankTransfer",
            "CIB Bank Transfer",
            "Account: 1234567890",
            "Transfer to CIB account and attach the receipt",
            null,
            sortOrder: 1);
        var payment = SubscriptionPayment.Create(Guid.NewGuid(), PaymentPurpose.Activation, 100m, "EGP", DateTimeOffset.UtcNow);

        payment.SubmitProof(Guid.NewGuid(), PaymentMethodSnapshot.From(paymentMethod), Guid.NewGuid(), DateTimeOffset.UtcNow);

        payment.PaymentMethodId.Should().Be(paymentMethod.Id);
        payment.PaymentMethodType.Should().Be(paymentMethod.Type);
        payment.PaymentMethodDisplayName.Should().Be(paymentMethod.DisplayName);
        payment.PaymentMethodValue.Should().Be(paymentMethod.Value);
        payment.PaymentMethodDescription.Should().Be(paymentMethod.Description);
    }
}
