using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Options;
using Xunit;

namespace Subscriptions.Tests;

public class StripePaymentGatewayTests
{
    private const string Secret = "whsec_CHANGE_ME_unit_test_secret";
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static StripePaymentGateway Gateway(HttpMessageHandler? handler = null, Action<PaymentGatewayOptions>? tweak = null)
    {
        var settings = new PaymentGatewayOptions { Enabled = true, Secret = Secret, ApiKey = "sk_test_CHANGE_ME", WebhookToleranceSeconds = 300 };
        tweak?.Invoke(settings);
        var options = new SubscriptionOptions();
        options.PaymentGateways["Stripe"] = settings;
        return new StripePaymentGateway(
            Microsoft.Extensions.Options.Options.Create(options), new StubHttpClientFactory(handler), new FixedTime(Now));
    }

    /// <summary>Stripe's documented scheme: <c>t=ts,v1=hex(HMAC-SHA256(secret, "ts.body"))</c>.</summary>
    private static string Sign(string body, DateTimeOffset at, string secret = Secret)
    {
        var timestamp = at.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }

    private static WebhookParseResult Parse(string body, string? signature) =>
        Gateway().ParseWebhook(new WebhookRequest(
            body,
            signature is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Stripe-Signature"] = signature },
            new Dictionary<string, string>()));

    private static WebhookParseResult ParseSigned(string body) => Parse(body, Sign(body, Now));

    private static string Event(string type, string objectJson, string id = "evt_1") =>
        $$$"""{"id":"{{{id}}}","object":"event","api_version":"2025-01-27.acacia","type":"{{{type}}}","data":{"object":{{{objectJson}}}}}""";

    private const string PaidSession =
        """{"id":"cs_test_1","object":"checkout.session","payment_status":"paid","amount_total":11500,"currency":"sar","payment_intent":"pi_1"}""";

    // ----- signature -----

    [Fact]
    public void Valid_checkout_completed_is_mapped_with_the_payment_intent_remembered()
    {
        var result = ParseSigned(Event("checkout.session.completed", PaidSession));

        result.IsValid.Should().BeTrue();
        result.Event.Should().Be(new PaymentWebhookEvent(
            "evt_1", PaymentWebhookEventType.PaymentSucceeded, "cs_test_1", 115.00m, "SAR",
            ProviderRefundId: null, PaymentReference: "pi_1"));
    }

    [Fact]
    public void Tampered_body_fails_the_signature()
    {
        var body = Event("checkout.session.completed", PaidSession);
        var signature = Sign(body, Now);

        Parse(body.Replace("11500", "100"), signature).Failure.Should().Be(WebhookFailure.InvalidSignature);
    }

    [Fact]
    public void Wrong_secret_missing_and_garbage_headers_fail_without_throwing()
    {
        var body = Event("checkout.session.completed", PaidSession);

        Parse(body, Sign(body, Now, "whsec_other")).Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(body, null).Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(body, "garbage").Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(body, "t=abc,v1=zz").Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(body, "").Failure.Should().Be(WebhookFailure.InvalidSignature);
        Gateway(tweak: s => s.Secret = null).ParseWebhook(new WebhookRequest(
                body, new Dictionary<string, string> { ["Stripe-Signature"] = Sign(body, Now) }, new Dictionary<string, string>()))
            .Failure.Should().Be(WebhookFailure.InvalidSignature);
    }

    [Fact]
    public void Stale_timestamp_is_refused_even_with_a_correct_signature()
    {
        var body = Event("checkout.session.completed", PaidSession);

        Parse(body, Sign(body, Now.AddSeconds(-301))).Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(body, Sign(body, Now.AddSeconds(-299))).IsValid.Should().BeTrue("inside the tolerance window");
    }

    [Fact]
    public void Tolerance_is_configurable()
    {
        var body = Event("checkout.session.completed", PaidSession);
        var request = new WebhookRequest(
            body, new Dictionary<string, string> { ["Stripe-Signature"] = Sign(body, Now.AddSeconds(-60)) }, new Dictionary<string, string>());

        Gateway(tweak: s => s.WebhookToleranceSeconds = 30).ParseWebhook(request).Failure.Should().Be(WebhookFailure.InvalidSignature);
        Gateway(tweak: s => s.WebhookToleranceSeconds = 120).ParseWebhook(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Signed_body_that_is_not_an_event_is_malformed()
    {
        ParseSigned("{}").Failure.Should().Be(WebhookFailure.MalformedPayload);
        ParseSigned("not json").Failure.Should().Be(WebhookFailure.MalformedPayload);
        ParseSigned(Event("checkout.session.completed", """{"payment_status":"paid"}""")).Failure
            .Should().Be(WebhookFailure.MalformedPayload, "a paid session without id / amount is unusable");
    }

    // ----- mapping -----

    [Fact]
    public void Completed_session_of_a_delayed_method_is_not_a_payment_yet()
    {
        var unpaid = PaidSession.Replace("\"paid\"", "\"unpaid\"");

        ParseSigned(Event("checkout.session.completed", unpaid)).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
        ParseSigned(Event("checkout.session.async_payment_succeeded", PaidSession)).Event!.Type
            .Should().Be(PaymentWebhookEventType.PaymentSucceeded);
    }

    [Fact]
    public void Expired_and_async_failed_sessions_are_failed_payments()
    {
        const string session = """{"id":"cs_test_1","payment_status":"unpaid","amount_total":11500,"currency":"sar"}""";

        foreach (var type in new[] { "checkout.session.expired", "checkout.session.async_payment_failed" })
        {
            var evt = ParseSigned(Event(type, session)).Event!;
            evt.Type.Should().Be(PaymentWebhookEventType.PaymentFailed);
            evt.ProviderReference.Should().Be("cs_test_1");
        }
    }

    [Fact]
    public void Succeeded_refund_is_a_refund_of_that_amount_keyed_by_the_refund_id_against_the_payment_intent()
    {
        const string refund = """{"id":"re_1","object":"refund","amount":3000,"currency":"sar","payment_intent":"pi_1","status":"succeeded"}""";

        var evt = ParseSigned(Event("refund.created", refund)).Event!;

        evt.Type.Should().Be(PaymentWebhookEventType.Refunded);
        evt.ProviderRefundId.Should().Be("re_1");
        evt.ProviderReference.Should().Be("pi_1");
        evt.Amount.Should().Be(30.00m);
        evt.Currency.Should().Be("SAR");
        ParseSigned(Event("refund.updated", refund, "evt_2")).Event!.ProviderRefundId.Should().Be("re_1");
    }

    [Fact]
    public void Pending_or_failed_refunds_and_charge_refunded_are_ignored_to_avoid_booking_one_refund_twice()
    {
        const string pending = """{"id":"re_1","amount":3000,"currency":"sar","payment_intent":"pi_1","status":"pending"}""";

        ParseSigned(Event("refund.created", pending)).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
        ParseSigned(Event("charge.refunded", """{"id":"ch_1","amount_refunded":3000}""")).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
        ParseSigned(Event("payment_intent.payment_failed", """{"id":"pi_1"}""")).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
        ParseSigned(Event("customer.created", """{"id":"cus_1"}""")).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
    }

    [Theory]
    [InlineData(11500L, "sar", 115.00)]
    [InlineData(1000L, "jpy", 1000.00)]
    [InlineData(5250L, "kwd", 5.25)]
    public void Minor_units_follow_the_currency_exponent(long minor, string currency, double expected)
    {
        var session = $$"""{"id":"cs_1","payment_status":"paid","amount_total":{{minor}},"currency":"{{currency}}"}""";

        ParseSigned(Event("checkout.session.completed", session)).Event!.Amount.Should().Be((decimal)expected);
    }

    // ----- API -----

    [Fact]
    public async Task Checkout_creates_a_payment_session_in_minor_units_with_our_payment_id_as_client_reference()
    {
        var handler = new StubHandler(_ => Json("""{"id":"cs_test_9","object":"checkout.session","url":"https://checkout.stripe.com/c/pay/cs_test_9"}"""));
        var paymentId = Guid.NewGuid();

        var session = await Gateway(handler).CreateCheckoutAsync(
            new CheckoutRequest(paymentId, Guid.NewGuid(), 115.00m, "SAR", "Activation payment",
                new Uri("https://app.example.com/ok"), new Uri("https://app.example.com/no")),
            CancellationToken.None);

        session.SessionId.Should().Be("cs_test_9");
        session.RedirectUrl.AbsoluteUri.Should().Be("https://checkout.stripe.com/c/pay/cs_test_9");

        var request = handler.Requests.Single();
        request.Uri.AbsoluteUri.Should().Be("https://api.stripe.com/v1/checkout/sessions");
        request.Authorization.Should().Be("Bearer sk_test_CHANGE_ME");
        var form = Uri.UnescapeDataString(request.Body);
        form.Should().Contain("mode=payment").And.Contain($"client_reference_id={paymentId}")
            .And.Contain("line_items[0][price_data][unit_amount]=11500")
            .And.Contain("line_items[0][price_data][currency]=sar");
    }

    [Fact]
    public async Task Checkout_failures_are_gateway_errors()
    {
        var request = new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), 10m, "SAR", "x", new Uri("https://a.test/ok"), new Uri("https://a.test/no"));
        var declined = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"type":"invalid_request_error","message":"Invalid currency"}}""")
        });

        await Gateway(declined).Invoking(g => g.CreateCheckoutAsync(request, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>().WithMessage("*Invalid currency*");
        await Gateway(tweak: s => s.ApiKey = null).Invoking(g => g.CreateCheckoutAsync(request, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>();
        await Gateway(new StubHandler(_ => Json("""{"id":"cs_1","object":"checkout.session"}"""))).Invoking(g => g.CreateCheckoutAsync(request, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>("a session without a hosted URL is unusable");
    }

    [Fact]
    public async Task Refund_targets_the_payment_intent_with_the_idempotency_key()
    {
        var handler = new StubHandler(_ => Json("""{"id":"re_77","object":"refund","amount":3000,"currency":"sar","status":"succeeded"}"""));

        var result = await Gateway(handler).RefundAsync(
            new RefundRequest("cs_test_1", "pi_1", 30m, "SAR", "refund-key-1"), CancellationToken.None);

        result.Should().Be(new RefundResult("re_77", 30m));
        var request = handler.Requests.Single();
        request.Uri.AbsoluteUri.Should().Be("https://api.stripe.com/v1/refunds");
        Uri.UnescapeDataString(request.Body).Should().Contain("payment_intent=pi_1").And.Contain("amount=3000");
        request.Headers["Idempotency-Key"].Should().Be("refund-key-1");
    }

    [Fact]
    public async Task Refund_failures_are_gateway_errors()
    {
        var request = new RefundRequest("cs_test_1", "pi_1", 30m, "SAR", "k");
        var declined = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"type":"invalid_request_error","message":"Charge already refunded"}}""")
        });

        await Gateway(declined).Invoking(g => g.RefundAsync(request, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>().WithMessage("*already refunded*");
        await Gateway(declined).Invoking(g => g.RefundAsync(request with { PaymentReference = null }, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>("the payment intent is unknown");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
