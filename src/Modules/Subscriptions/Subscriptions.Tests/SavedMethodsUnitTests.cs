using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Options;
using Xunit;

namespace Subscriptions.Tests;

public class SavedMethodsUnitTests
{
    private static FakePaymentGateway Fake()
    {
        var options = new SubscriptionOptions();
        options.PaymentGateways["Fake"] = new PaymentGatewayOptions { Enabled = true, Secret = "unit-test-secret-0123456789" };
        return new FakePaymentGateway(Microsoft.Extensions.Options.Options.Create(options));
    }

    private static OffSessionChargeRequest Charge(string methodRef) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 10m, "SAR", "cus_1", methodRef, "dunning:p:day-1", "dunning:p:day-1", "Renewal payment");

    [Theory]
    [InlineData("pm_ok", OffSessionChargeStatus.Succeeded)]
    [InlineData("pm_declined", OffSessionChargeStatus.Declined)]
    [InlineData("pm_action", OffSessionChargeStatus.RequiresAction)]
    [InlineData("pm_transient", OffSessionChargeStatus.Transient)]
    public async Task Fake_gateway_decides_the_off_session_outcome_from_the_method_reference(string methodRef, OffSessionChargeStatus status)
    {
        var result = await Fake().ChargeSavedMethodAsync(Charge(methodRef), CancellationToken.None);

        result.Status.Should().Be(status);
    }

    [Fact]
    public void Fake_webhook_carries_the_saved_card_capture_on_success_only()
    {
        const string secret = "unit-test-secret-0123456789";
        var body = JsonSerializer.Serialize(new
        {
            id = "e1",
            type = "payment.succeeded",
            reference = "s1",
            amount = 5m,
            currency = "SAR",
            customer_reference = "cus_1",
            method_reference = "pm_1",
            card_brand = "visa",
            card_last4 = "4242",
            card_exp_month = 12,
            card_exp_year = 2030
        });

        var result = Fake().ParseWebhook(new WebhookRequest(body, new Dictionary<string, string>
        {
            [FakePaymentGateway.SignatureHeader] = FakePaymentGateway.Sign(secret, body)
        }));

        result.Event!.SavedMethod.Should().Be(new SavedMethodCapture("cus_1", "pm_1", "visa", "4242", 12, 2030));
    }

    [Fact]
    public void Stripe_off_session_payment_intent_events_map_to_the_session_reference()
    {
        const string succeeded = """
            {"id":"evt_1","type":"payment_intent.succeeded","data":{"object":{"id":"pi_9","amount_received":11500,"currency":"sar",
            "metadata":{"source":"off_session","session_ref":"dunning:abc:day-1"}}}}
            """;
        const string failed = """
            {"id":"evt_2","type":"payment_intent.payment_failed","data":{"object":{"id":"pi_9","amount":11500,"currency":"sar",
            "metadata":{"source":"off_session","session_ref":"dunning:abc:day-1"}}}}
            """;

        var ok = StripePaymentGateway.Map(succeeded).Event!;
        var ko = StripePaymentGateway.Map(failed).Event!;

        ok.Type.Should().Be(PaymentWebhookEventType.PaymentSucceeded);
        ok.ProviderReference.Should().Be("dunning:abc:day-1");
        ok.PaymentReference.Should().Be("pi_9");
        ok.Amount.Should().Be(115m);
        ko.Type.Should().Be(PaymentWebhookEventType.PaymentFailed);
    }

    [Fact]
    public void Stripe_payment_intent_events_of_checkout_payments_are_ignored()
    {
        const string checkoutIntent = """
            {"id":"evt_3","type":"payment_intent.succeeded","data":{"object":{"id":"pi_1","amount_received":100,"currency":"usd","metadata":{}}}}
            """;

        StripePaymentGateway.Map(checkoutIntent).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
    }

    [Fact]
    public void Stripe_checkout_completion_reports_the_customer_only_when_saving_was_requested()
    {
        const string template = """
            {"id":"evt_4","type":"checkout.session.completed","data":{"object":{"id":"cs_1","payment_status":"paid","amount_total":1000,
            "currency":"usd","customer":"cus_7","payment_intent":"pi_7","metadata":{METADATA}}}}
            """;

        var saved = StripePaymentGateway.Map(template.Replace("METADATA", "\"save_method\":\"true\"")).Event!;
        var plain = StripePaymentGateway.Map(template.Replace("METADATA", "")).Event!;

        saved.SavedMethod.Should().Be(new SavedMethodCapture("cus_7", null));
        plain.SavedMethod.Should().BeNull();
    }

    [Fact]
    public void Saved_method_is_expired_after_the_end_of_its_expiry_month()
    {
        var method = SavedPaymentMethod.Create(Guid.NewGuid(), "Stripe", "cus", "pm", "visa", "4242", 6, 2026, true, DateTimeOffset.UtcNow);

        method.IsExpired(new DateTimeOffset(2026, 6, 30, 23, 0, 0, TimeSpan.Zero)).Should().BeFalse();
        method.IsExpired(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
    }

    [Fact]
    public void A_revoked_method_is_never_the_default_and_cannot_become_one()
    {
        var method = SavedPaymentMethod.Create(Guid.NewGuid(), "Stripe", "cus", "pm", null, null, null, null, true, DateTimeOffset.UtcNow);

        method.Revoke(DateTimeOffset.UtcNow);

        method.IsDefault.Should().BeFalse();
        method.IsActive.Should().BeFalse();
        method.Invoking(m => m.SetDefault(true)).Should().Throw<Exception>();
    }

    [Fact]
    public void Attempt_idempotency_key_is_stable_per_payment_and_step()
    {
        var paymentId = Guid.NewGuid();

        PaymentAttempt.KeyFor(paymentId, "day-3").Should().Be($"dunning:{paymentId}:day-3");
    }
}
