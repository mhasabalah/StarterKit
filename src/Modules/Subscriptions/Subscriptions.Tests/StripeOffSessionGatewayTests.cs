using System.Net;
using System.Text;
using FluentAssertions;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Options;
using Xunit;

namespace Subscriptions.Tests;

/// <summary>
/// The real Stripe adapter against a fake HTTP endpoint (Stripe.net over a stub handler): asserts the wire requests of
/// the saved-card / off-session paths and maps canned Stripe JSON. A live Stripe test-account call is still recommended.
/// </summary>
public class StripeOffSessionGatewayTests
{
    private sealed class FixedTime : TimeProvider;

    private static StripePaymentGateway Gateway(HttpMessageHandler handler)
    {
        var options = new SubscriptionOptions();
        options.PaymentGateways["Stripe"] = new PaymentGatewayOptions { Enabled = true, Secret = "whsec_CHANGE_ME", ApiKey = "sk_test_CHANGE_ME" };
        return new StripePaymentGateway(
            Microsoft.Extensions.Options.Options.Create(options), new StubHttpClientFactory(handler), new FixedTime());
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Intent(string status) =>
        "{\"id\":\"pi_3Qx\",\"object\":\"payment_intent\",\"amount\":11500,\"currency\":\"sar\",\"customer\":\"cus_1\","
        + $"\"status\":\"{status}\",\"last_payment_error\":null,\"payment_method\":\"pm_1\"}}";

    private static string ErrorBody(string type, string? code, string message, string? intentJson = null) =>
        "{\"error\":{\"type\":\"" + type + "\""
        + (code is null ? "" : ",\"code\":\"" + code + "\"")
        + ",\"message\":\"" + message + "\""
        + (intentJson is null ? "" : ",\"payment_intent\":" + intentJson)
        + "}}";

    private static OffSessionChargeRequest Charge(decimal amount, string currency, Guid? paymentId = null) =>
        new(paymentId ?? Guid.NewGuid(), Guid.NewGuid(), amount, currency, "cus_1", "pm_1", "dunning:abc:2", "Renewal");

    private static Dictionary<string, string> Form(StubHandler.Captured request) =>
        request.Body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(
                p => Uri.UnescapeDataString(p[0].Replace('+', ' ')),
                p => Uri.UnescapeDataString((p.Length > 1 ? p[1] : "").Replace('+', ' ')));

    // ----- ChargeSavedMethodAsync: request -----

    [Fact]
    public async Task Charge_posts_a_confirmed_off_session_payment_intent_with_the_idempotency_key()
    {
        var handler = new StubHandler(_ => Json(Intent("succeeded")));
        var paymentId = Guid.NewGuid();
        var request = Charge(115.00m, "SAR", paymentId) with { IdempotencyKey = $"dunning:{paymentId}:2" };

        var result = await Gateway(handler).ChargeSavedMethodAsync(request, CancellationToken.None);

        result.Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.Succeeded, "pi_3Qx"));
        var sent = handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.AbsolutePath.Should().Be("/v1/payment_intents");
        sent.Authorization.Should().Be("Bearer sk_test_CHANGE_ME");
        sent.Headers["Idempotency-Key"].Should().Be($"dunning:{paymentId}:2");
        var form = Form(sent);
        form["amount"].Should().Be("11500");
        form["currency"].Should().Be("sar");
        form["customer"].Should().Be("cus_1");
        form["payment_method"].Should().Be("pm_1");
        form["off_session"].Should().Be("true");
        form["confirm"].Should().Be("true");
        form["metadata[source]"].Should().Be("off_session");
        form["metadata[session_ref]"].Should().Be($"dunning:{paymentId}:2");
        form["metadata[payment_id]"].Should().Be(paymentId.ToString());
        form.Should().NotContainKey("setup_future_usage");
    }

    [Theory]
    [InlineData("KWD", 12.340, "12340")]   // three-decimal
    [InlineData("JPY", 1500, "1500")]      // zero-decimal
    [InlineData("MGA", 20000, "20000")]    // zero-decimal at Stripe, two-decimal in ISO
    [InlineData("USD", 19.99, "1999")]
    [InlineData("ISK", 500, "50000")]      // legacy two-decimal
    public async Task Charge_sends_the_amount_in_stripes_minor_units(string currency, double amount, string expected)
    {
        var handler = new StubHandler(_ => Json(Intent("succeeded")));

        await Gateway(handler).ChargeSavedMethodAsync(Charge((decimal)amount, currency), CancellationToken.None);

        var form = Form(handler.Requests.Single());
        form["amount"].Should().Be(expected);
        form["currency"].Should().Be(currency.ToLowerInvariant());
    }

    // ----- ChargeSavedMethodAsync: responses -----

    [Fact]
    public async Task Charge_requires_action_maps_to_RequiresAction()
    {
        var result = await Gateway(new StubHandler(_ => Json(Intent("requires_action"))))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.RequiresAction, "pi_3Qx", "authentication_required"));
    }

    [Fact]
    public async Task Charge_processing_is_transient_until_the_webhook_settles_it()
    {
        var result = await Gateway(new StubHandler(_ => Json(Intent("processing"))))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.Transient, "pi_3Qx", "processing"));
    }

    [Fact]
    public async Task Charge_card_declined_error_is_Declined_with_the_code_and_intent()
    {
        var body = ErrorBody("card_error", "card_declined", "Your card was declined.", "{\"id\":\"pi_3Dec\",\"object\":\"payment_intent\"}");
        var result = await Gateway(new StubHandler(_ => Json(body, HttpStatusCode.PaymentRequired)))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.Declined, "pi_3Dec", "card_declined"));
    }

    [Fact]
    public async Task Charge_authentication_required_card_error_is_RequiresAction()
    {
        var body = ErrorBody("card_error", "authentication_required", "This payment requires authentication.", "{\"id\":\"pi_3Auth\",\"object\":\"payment_intent\"}");
        var result = await Gateway(new StubHandler(_ => Json(body, HttpStatusCode.PaymentRequired)))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.RequiresAction, "pi_3Auth", "authentication_required"));
    }

    [Fact]
    public async Task Charge_invalid_request_error_is_a_distinct_method_unusable_failure()
    {
        var body = ErrorBody("invalid_request_error", "resource_missing", "No such PaymentMethod: pm_1");
        var result = await Gateway(new StubHandler(_ => Json(body, HttpStatusCode.BadRequest)))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Status.Should().Be(OffSessionChargeStatus.Declined);
        result.FailureCode.Should().Be("invalid_request:resource_missing");
    }

    [Fact]
    public async Task Charge_server_error_is_Transient()
    {
        var body = ErrorBody("api_error", null, "An unknown error occurred");
        var result = await Gateway(new StubHandler(_ => Json(body, HttpStatusCode.InternalServerError)))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Status.Should().Be(OffSessionChargeStatus.Transient);
    }

    [Fact]
    public async Task Charge_unparseable_5xx_is_Transient()
    {
        var result = await Gateway(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") }))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Status.Should().Be(OffSessionChargeStatus.Transient);
    }

    [Fact]
    public async Task Charge_network_failure_is_Transient()
    {
        var result = await Gateway(new StubHandler(_ => throw new HttpRequestException("connection refused")))
            .ChargeSavedMethodAsync(Charge(10m, "SAR"), CancellationToken.None);

        result.Status.Should().Be(OffSessionChargeStatus.Transient);
    }

    [Fact]
    public async Task Network_failures_on_status_and_describe_are_swallowed_as_unknown()
    {
        var gateway = Gateway(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        (await gateway.GetChargeStatusAsync("pi_3Qx", CancellationToken.None))
            .Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.Transient, "pi_3Qx", "status_unknown"));
        (await gateway.DescribeSavedMethodAsync(new SavedMethodCapture("cus_1", null), "pi_3Qx", CancellationToken.None))
            .Should().BeNull();
    }

    // ----- GetChargeStatusAsync -----

    [Theory]
    [InlineData("succeeded", OffSessionChargeStatus.Succeeded)]
    [InlineData("processing", OffSessionChargeStatus.Transient)]
    [InlineData("requires_action", OffSessionChargeStatus.RequiresAction)]
    [InlineData("requires_payment_method", OffSessionChargeStatus.Declined)]
    [InlineData("canceled", OffSessionChargeStatus.Declined)]
    public async Task GetChargeStatus_reads_the_intent_and_maps_its_status(string status, OffSessionChargeStatus expected)
    {
        var handler = new StubHandler(_ => Json(Intent(status)));

        var result = await Gateway(handler).GetChargeStatusAsync("pi_3Qx", CancellationToken.None);

        result.Status.Should().Be(expected);
        var sent = handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Uri.AbsolutePath.Should().Be("/v1/payment_intents/pi_3Qx");
    }

    [Fact]
    public async Task GetChargeStatus_uses_the_last_payment_error_code_when_declined()
    {
        var body = Intent("requires_payment_method").Replace(
            "\"last_payment_error\":null",
            "\"last_payment_error\":{\"type\":\"card_error\",\"code\":\"insufficient_funds\"}");

        var result = await Gateway(new StubHandler(_ => Json(body))).GetChargeStatusAsync("pi_3Qx", CancellationToken.None);

        result.Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.Declined, "pi_3Qx", "insufficient_funds"));
    }

    [Fact]
    public async Task GetChargeStatus_provider_failure_is_Transient_status_unknown()
    {
        var result = await Gateway(new StubHandler(_ => Json(ErrorBody("api_error", null, "boom"), HttpStatusCode.InternalServerError)))
            .GetChargeStatusAsync("pi_3Qx", CancellationToken.None);

        result.Should().Be(new OffSessionChargeResult(OffSessionChargeStatus.Transient, "pi_3Qx", "status_unknown"));
    }

    // ----- DescribeSavedMethodAsync -----

    [Fact]
    public async Task DescribeSavedMethod_expands_the_payment_method_of_the_intent()
    {
        var expanded = "\"payment_method\":{\"id\":\"pm_9\",\"object\":\"payment_method\",\"type\":\"card\","
                       + "\"card\":{\"brand\":\"visa\",\"last4\":\"4242\",\"exp_month\":8,\"exp_year\":2030}}";
        var body = Intent("succeeded").Replace("\"payment_method\":\"pm_1\"", expanded);
        var handler = new StubHandler(_ => Json(body));

        var capture = await Gateway(handler).DescribeSavedMethodAsync(new SavedMethodCapture("cus_1", null), "pi_3Qx", CancellationToken.None);

        capture.Should().Be(new SavedMethodCapture("cus_1", "pm_9", "visa", "4242", 8, 2030));
        var sent = handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Uri.AbsolutePath.Should().Be("/v1/payment_intents/pi_3Qx");
        Uri.UnescapeDataString(sent.Uri.Query).Should().Contain("expand[0]=payment_method");
    }

    [Fact]
    public async Task DescribeSavedMethod_skips_the_call_when_the_method_is_known_or_there_is_no_payment()
    {
        var handler = new StubHandler(_ => Json(Intent("succeeded")));
        var gateway = Gateway(handler);
        var known = new SavedMethodCapture("cus_1", "pm_1");

        (await gateway.DescribeSavedMethodAsync(known, "pi_3Qx", CancellationToken.None)).Should().BeSameAs(known);
        (await gateway.DescribeSavedMethodAsync(new SavedMethodCapture("cus_1", null), null, CancellationToken.None)).Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task DescribeSavedMethod_is_best_effort_when_stripe_fails()
    {
        var result = await Gateway(new StubHandler(_ => Json(ErrorBody("api_error", null, "boom"), HttpStatusCode.InternalServerError)))
            .DescribeSavedMethodAsync(new SavedMethodCapture("cus_1", null), "pi_3Qx", CancellationToken.None);

        result.Should().BeNull();
    }

    // ----- DetachAsync -----

    [Fact]
    public async Task Detach_posts_to_the_payment_method_detach_endpoint()
    {
        var handler = new StubHandler(_ => Json("{\"id\":\"pm_1\",\"object\":\"payment_method\",\"customer\":null,\"type\":\"card\"}"));

        await Gateway(handler).DetachAsync("cus_1", "pm_1", CancellationToken.None);

        var sent = handler.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.AbsolutePath.Should().Be("/v1/payment_methods/pm_1/detach");
    }

    [Fact]
    public async Task Detach_failure_is_a_gateway_error()
    {
        var body = ErrorBody("invalid_request_error", "resource_missing", "No such PaymentMethod: pm_1");

        await Gateway(new StubHandler(_ => Json(body, HttpStatusCode.NotFound)))
            .Invoking(g => g.DetachAsync("cus_1", "pm_1", CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>().WithMessage("*No such PaymentMethod*");
    }

    // ----- Checkout with SaveCard -----

    private static CheckoutRequest Checkout(bool save, string? customer = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 115m, "SAR", "Activation", new Uri("https://app.example.com/ok"),
            new Uri("https://app.example.com/no"), save, customer);

    private const string SessionJson =
        "{\"id\":\"cs_test_9\",\"object\":\"checkout.session\",\"url\":\"https://checkout.stripe.com/c/pay/cs_test_9\"}";

    [Fact]
    public async Task Checkout_with_save_card_and_a_known_customer_reuses_it_and_sets_off_session_usage()
    {
        var handler = new StubHandler(_ => Json(SessionJson));

        await Gateway(handler).CreateCheckoutAsync(Checkout(true, "cus_1"), CancellationToken.None);

        var form = Form(handler.Requests.Single());
        form["customer"].Should().Be("cus_1");
        form["payment_intent_data[setup_future_usage]"].Should().Be("off_session");
        form["metadata[save_method]"].Should().Be("true");
        form.Should().NotContainKey("customer_creation");
    }

    [Fact]
    public async Task Checkout_with_save_card_and_no_customer_asks_stripe_to_create_one()
    {
        var handler = new StubHandler(_ => Json(SessionJson));

        await Gateway(handler).CreateCheckoutAsync(Checkout(true), CancellationToken.None);

        var form = Form(handler.Requests.Single());
        form["customer_creation"].Should().Be("always");
        form["payment_intent_data[setup_future_usage]"].Should().Be("off_session");
        form.Should().NotContainKey("customer");
    }

    [Fact]
    public async Task Checkout_without_save_card_sends_no_saved_card_fields()
    {
        var handler = new StubHandler(_ => Json(SessionJson));

        await Gateway(handler).CreateCheckoutAsync(Checkout(false, "cus_1"), CancellationToken.None);

        var form = Form(handler.Requests.Single());
        form.Keys.Should().NotContain(k => k.Contains("setup_future_usage"));
        form.Should().NotContainKey("customer").And.NotContainKey("customer_creation").And.NotContainKey("metadata[save_method]");
    }
}
