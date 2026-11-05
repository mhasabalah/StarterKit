using FluentAssertions;
using Xunit;
using Microsoft.Extensions.Options;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Options;

namespace Subscriptions.Tests;

internal static class FakeGatewayTestExtensions
{
    /// <summary>Body + headers shorthand (no query string) for the request-based port.</summary>
    public static WebhookParseResult ParseWebhook(
        this FakePaymentGateway gateway, string body, IReadOnlyDictionary<string, string> headers) =>
        gateway.ParseWebhook(new WebhookRequest(body, headers));
}

public class FakePaymentGatewayTests
{
    private const string Secret = "unit-test-secret-0123456789";
    private const string Valid = """{"id":"e1","type":"payment.succeeded","reference":"s1","amount":12.50,"currency":"sar"}""";

    private static FakePaymentGateway Gateway()
    {
        var options = new SubscriptionOptions();
        options.PaymentGateways["fake"] = new PaymentGatewayOptions { Enabled = true, Secret = Secret };
        return new FakePaymentGateway(Microsoft.Extensions.Options.Options.Create(options));
    }

    private static Dictionary<string, string> Signed(string body, string? secret = null) =>
        new(StringComparer.OrdinalIgnoreCase) { [FakePaymentGateway.SignatureHeader] = FakePaymentGateway.Sign(secret ?? Secret, body) };

    [Fact]
    public void Valid_signature_yields_a_normalized_event()
    {
        var result = Gateway().ParseWebhook(Valid, Signed(Valid));

        result.IsValid.Should().BeTrue();
        result.Event.Should().Be(new PaymentWebhookEvent("e1", PaymentWebhookEventType.PaymentSucceeded, "s1", 12.50m, "SAR"));
    }

    [Fact]
    public void Tampered_body_fails_the_signature()
    {
        var headers = Signed(Valid);

        Gateway().ParseWebhook(Valid.Replace("12.50", "0.01"), headers).Failure.Should().Be(WebhookFailure.InvalidSignature);
    }

    [Fact]
    public void Wrong_secret_missing_header_and_garbage_signature_all_fail_without_throwing()
    {
        var gateway = Gateway();

        gateway.ParseWebhook(Valid, Signed(Valid, "another-secret")).Failure.Should().Be(WebhookFailure.InvalidSignature);
        gateway.ParseWebhook(Valid, new Dictionary<string, string>()).Failure.Should().Be(WebhookFailure.InvalidSignature);
        gateway.ParseWebhook(Valid, new Dictionary<string, string> { [FakePaymentGateway.SignatureHeader] = "zz" })
            .Failure.Should().Be(WebhookFailure.InvalidSignature);
    }

    [Fact]
    public void Signed_but_incomplete_body_is_malformed()
    {
        const string body = """{"id":"e1"}""";

        Gateway().ParseWebhook(body, Signed(body)).Failure.Should().Be(WebhookFailure.MalformedPayload);
    }

    [Fact]
    public void Unknown_type_is_normalized_to_other()
    {
        var body = Valid.Replace("payment.succeeded", "something.else");

        Gateway().ParseWebhook(body, Signed(body)).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
    }

    [Fact]
    public async Task Checkout_returns_a_session_and_a_redirect_url()
    {
        var session = await Gateway().CreateCheckoutAsync(
            new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), 10m, "SAR", "x", new Uri("https://a.test/ok"), new Uri("https://a.test/no")),
            CancellationToken.None);

        session.SessionId.Should().StartWith("fake_");
        session.RedirectUrl.AbsoluteUri.Should().Contain(session.SessionId);
    }

    [Fact]
    public void Refund_event_carries_the_provider_refund_id_and_the_amount_of_that_refund()
    {
        const string body = """{"id":"e9","type":"refund.created","reference":"s1","amount":30.00,"currency":"sar","refund_id":"re_1"}""";

        var evt = Gateway().ParseWebhook(body, Signed(body)).Event!;

        evt.Type.Should().Be(PaymentWebhookEventType.Refunded);
        evt.ProviderRefundId.Should().Be("re_1");
        evt.Amount.Should().Be(30m);
    }

    [Fact]
    public async Task Refund_returns_a_fresh_provider_refund_id_for_the_requested_amount()
    {
        var request = new RefundRequest("s1", null, 12.50m, "SAR", "key-1");

        var first = await Gateway().RefundAsync(request, CancellationToken.None);
        var second = await Gateway().RefundAsync(request, CancellationToken.None);

        first.Amount.Should().Be(12.50m);
        first.RefundId.Should().StartWith("fake_re_").And.NotBe(second.RefundId);
    }
}
