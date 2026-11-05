using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Options;
using Xunit;

namespace Subscriptions.Tests;

public class PaymobPaymentGatewayTests
{
    private const string Secret = "paymob-hmac-secret-0123456789";

    /// <summary>The HMAC of <see cref="SampleBody"/> computed independently (Python hmac/sha512 over the documented order).</summary>
    private const string SampleHmac =
        "b59ed0d7d7e9dcebaa7ad776a070196710fdf9bb1c300da135eeb46225a1cf751d022ef98b0fc139281ccbc91c7b612e29de5ad313cab9f5fd92382cb609cda5";

    private const string SampleBody = """
        {"type": "TRANSACTION", "obj": {"id": 424242, "pending": false, "amount_cents": 11500, "success": true, "is_auth": false,
        "is_capture": false, "is_standalone_payment": true, "is_voided": false, "is_refunded": false, "is_3d_secure": true,
        "integration_id": 4111111, "has_parent_transaction": false, "owner": 99887, "error_occured": false,
        "created_at": "2026-11-04T10:15:30.123456", "currency": "EGP", "order": {"id": 5550001, "merchant_order_id": "x"},
        "source_data": {"pan": "2346", "sub_type": "MasterCard", "type": "card"}, "parent_transaction": null}}
        """;

    private static PaymobPaymentGateway Gateway(HttpMessageHandler? handler = null, Action<PaymentGatewayOptions>? tweak = null)
    {
        var settings = new PaymentGatewayOptions
        {
            Enabled = true, Secret = Secret, ApiKey = "egy_sk_test_CHANGE_ME", PublicKey = "egy_pk_test_CHANGE_ME",
            IntegrationIds = [4111111]
        };
        tweak?.Invoke(settings);
        var options = new SubscriptionOptions();
        options.PaymentGateways["Paymob"] = settings;
        return new PaymobPaymentGateway(Microsoft.Extensions.Options.Options.Create(options), new StubHttpClientFactory(handler));
    }

    private static Dictionary<string, string> Hmac(string hmac) => new(StringComparer.OrdinalIgnoreCase) { ["hmac"] = hmac };

    /// <summary>The sample with <paramref name="change"/> applied to <c>obj</c>, signed correctly.</summary>
    private static (string Body, Dictionary<string, string> Query) Signed(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(SampleBody)!.AsObject();
        change(root["obj"]!.AsObject());
        var body = root.ToJsonString();
        using var document = JsonDocument.Parse(body);
        return (body, Hmac(PaymobPaymentGateway.Sign(Secret, document.RootElement.GetProperty("obj"))));
    }

    private static WebhookParseResult Parse(string body, Dictionary<string, string> query) =>
        Gateway().ParseWebhook(new WebhookRequest(body, new Dictionary<string, string>(), query));

    // ----- signature -----

    [Fact]
    public void Hmac_matches_an_independently_computed_vector()
    {
        using var document = JsonDocument.Parse(SampleBody);

        PaymobPaymentGateway.Sign(Secret, document.RootElement.GetProperty("obj")).Should().Be(SampleHmac);
    }

    [Fact]
    public void Valid_success_callback_is_mapped_from_signed_fields()
    {
        var result = Parse(SampleBody, Hmac(SampleHmac));

        result.IsValid.Should().BeTrue();
        result.Event.Should().Be(new PaymentWebhookEvent(
            "txn:424242:10000", PaymentWebhookEventType.PaymentSucceeded, "5550001", 115.00m, "EGP",
            ProviderRefundId: null, PaymentReference: "424242"));
    }

    [Fact]
    public void Upper_case_hmac_is_accepted()
    {
        Parse(SampleBody, Hmac(SampleHmac.ToUpperInvariant())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Tampered_amount_fails_the_signature()
    {
        var tampered = SampleBody.Replace("\"amount_cents\": 11500", "\"amount_cents\": 1");

        Parse(tampered, Hmac(SampleHmac)).Failure.Should().Be(WebhookFailure.InvalidSignature);
    }

    [Fact]
    public void Tampered_order_reference_fails_the_signature()
    {
        var tampered = SampleBody.Replace("\"id\": 5550001", "\"id\": 5550002");

        Parse(tampered, Hmac(SampleHmac)).Failure.Should().Be(WebhookFailure.InvalidSignature);
    }

    [Fact]
    public void Unsigned_fields_are_ignored_by_the_signature_and_never_trusted()
    {
        // parent_transaction is not part of the HMAC: changing it neither breaks the signature nor changes the event.
        var changed = SampleBody.Replace("\"parent_transaction\": null", "\"parent_transaction\": 1");

        var result = Parse(changed, Hmac(SampleHmac));

        result.Event!.ProviderReference.Should().Be("5550001");
        result.Event.PaymentReference.Should().Be("424242");
    }

    [Fact]
    public void Wrong_secret_missing_hmac_and_garbage_hmac_all_fail_without_throwing()
    {
        Gateway(tweak: s => s.Secret = "another-secret").ParseWebhook(new WebhookRequest(SampleBody, new Dictionary<string, string>(), Hmac(SampleHmac)))
            .Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(SampleBody, []).Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(SampleBody, Hmac("zz")).Failure.Should().Be(WebhookFailure.InvalidSignature);
        Parse(SampleBody, Hmac("")).Failure.Should().Be(WebhookFailure.InvalidSignature);
        Gateway(tweak: s => s.Secret = null).ParseWebhook(new WebhookRequest(SampleBody, new Dictionary<string, string>(), Hmac(SampleHmac)))
            .Failure.Should().Be(WebhookFailure.InvalidSignature);
    }

    [Fact]
    public void Body_that_is_not_json_or_has_no_transaction_is_malformed()
    {
        Parse("not json", Hmac(SampleHmac)).Failure.Should().Be(WebhookFailure.MalformedPayload);
        Parse("""{"type":"TOKEN"}""", Hmac(SampleHmac)).Failure.Should().Be(WebhookFailure.MalformedPayload);
    }

    [Fact]
    public void Signed_transaction_without_an_order_is_malformed()
    {
        var (body, query) = Signed(o => o.Remove("order"));

        Parse(body, query).Failure.Should().Be(WebhookFailure.MalformedPayload);
    }

    // ----- mapping -----

    [Fact]
    public void Declined_transaction_is_a_failed_payment()
    {
        var (body, query) = Signed(o => o["success"] = false);

        var evt = Parse(body, query).Event!;

        evt.Type.Should().Be(PaymentWebhookEventType.PaymentFailed);
        evt.ProviderReference.Should().Be("5550001");
    }

    [Fact]
    public void Pending_voided_and_auth_only_transactions_are_ignored()
    {
        foreach (var change in new Action<JsonObject>[]
                 {
                     o => { o["pending"] = true; o["success"] = false; },
                     o => o["is_voided"] = true,
                     o => o["is_auth"] = true
                 })
        {
            var (body, query) = Signed(change);
            Parse(body, query).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
        }
    }

    [Fact]
    public void Pending_and_final_callbacks_of_one_transaction_have_different_event_ids()
    {
        var (pendingBody, pendingQuery) = Signed(o => { o["pending"] = true; o["success"] = false; });

        Parse(pendingBody, pendingQuery).Event!.EventId.Should().NotBe(Parse(SampleBody, Hmac(SampleHmac)).Event!.EventId);
    }

    [Fact]
    public void Successful_child_transaction_with_is_refunded_is_a_refund_keyed_by_its_own_transaction_id()
    {
        var (body, query) = Signed(o =>
        {
            o["id"] = 777001;
            o["has_parent_transaction"] = true;
            o["is_refunded"] = true;
            o["amount_cents"] = 3000;
        });

        var evt = Parse(body, query).Event!;

        evt.Type.Should().Be(PaymentWebhookEventType.Refunded);
        evt.ProviderRefundId.Should().Be("777001");
        evt.Amount.Should().Be(30.00m, "the amount of THIS refund, not of the payment");
        evt.ProviderReference.Should().Be("5550001");
    }

    [Fact]
    public void Parent_updated_after_a_refund_and_failed_child_are_not_refunds()
    {
        var (parentBody, parentQuery) = Signed(o => o["is_refunded"] = true);
        var (failedChildBody, failedChildQuery) = Signed(o =>
        {
            o["has_parent_transaction"] = true;
            o["is_refunded"] = true;
            o["success"] = false;
        });

        Parse(parentBody, parentQuery).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
        Parse(failedChildBody, failedChildQuery).Event!.Type.Should().Be(PaymentWebhookEventType.Other);
    }

    // ----- API -----

    [Fact]
    public async Task Checkout_posts_an_intention_in_cents_and_redirects_to_the_unified_checkout()
    {
        var handler = new StubHandler(_ => Json("""{"id":"int_1","client_secret":"cs_abc","intention_order_id":12345}"""));
        var paymentId = Guid.NewGuid();

        var session = await Gateway(handler).CreateCheckoutAsync(
            new CheckoutRequest(paymentId, Guid.NewGuid(), 115.00m, "egp", "Activation payment",
                new Uri("https://app.example.com/ok"), new Uri("https://app.example.com/no")),
            CancellationToken.None);

        session.SessionId.Should().Be("12345", "the intention's ORDER id is what the callback's order.id reports");
        session.RedirectUrl.AbsoluteUri.Should().StartWith("https://accept.paymob.com/unifiedcheckout/")
            .And.Contain("publicKey=egy_pk_test_CHANGE_ME").And.Contain("clientSecret=cs_abc");

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.AbsoluteUri.Should().Be("https://accept.paymob.com/v1/intention/");
        request.Authorization.Should().Be("Token egy_sk_test_CHANGE_ME");
        var body = JsonNode.Parse(request.Body)!;
        ((long)body["amount"]!).Should().Be(11500);
        ((string)body["currency"]!).Should().Be("EGP");
        ((long)body["payment_methods"]![0]!).Should().Be(4111111);
        ((string)body["special_reference"]!).Should().StartWith(paymentId.ToString("N"));
    }

    [Fact]
    public async Task Checkout_refused_by_paymob_or_unconfigured_is_a_gateway_error()
    {
        var refused = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") });
        var request = new CheckoutRequest(Guid.NewGuid(), Guid.NewGuid(), 10m, "EGP", "x", new Uri("https://a.test/ok"), new Uri("https://a.test/no"));

        await Gateway(refused).Invoking(g => g.CreateCheckoutAsync(request, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>();
        await Gateway(tweak: s => s.IntegrationIds = []).Invoking(g => g.CreateCheckoutAsync(request, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>();
        await Gateway(new StubHandler(_ => Json("""{"id":"x"}"""))).Invoking(g => g.CreateCheckoutAsync(request, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>("a response without client secret / order id is unusable");
    }

    [Fact]
    public async Task Refund_posts_the_transaction_and_amount_in_cents_and_returns_the_refund_transaction_id()
    {
        var handler = new StubHandler(_ => Json("""{"id":777001,"success":true,"amount_cents":3000}"""));

        var result = await Gateway(handler).RefundAsync(
            new RefundRequest("5550001", "424242", 30m, "EGP", "key"), CancellationToken.None);

        result.Should().Be(new RefundResult("777001", 30m));
        var request = handler.Requests.Single();
        request.Uri.AbsoluteUri.Should().Be("https://accept.paymob.com/api/acceptance/void_refund/refund");
        request.Authorization.Should().Be("Token egy_sk_test_CHANGE_ME");
        var body = JsonNode.Parse(request.Body)!;
        ((long)body["transaction_id"]!).Should().Be(424242);
        ((long)body["amount_cents"]!).Should().Be(3000);
    }

    [Fact]
    public async Task Refund_failures_are_gateway_errors()
    {
        var declined = new StubHandler(_ => Json("""{"id":1,"success":false}"""));
        var serverError = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var network = new StubHandler(_ => throw new HttpRequestException("down"));
        var request = new RefundRequest("5550001", "424242", 30m, "EGP", "key");

        await Gateway(declined).Invoking(g => g.RefundAsync(request, CancellationToken.None)).Should().ThrowAsync<PaymentGatewayException>();
        await Gateway(serverError).Invoking(g => g.RefundAsync(request, CancellationToken.None)).Should().ThrowAsync<PaymentGatewayException>();
        await Gateway(network).Invoking(g => g.RefundAsync(request, CancellationToken.None)).Should().ThrowAsync<PaymentGatewayException>();
        await Gateway(declined).Invoking(g => g.RefundAsync(request with { PaymentReference = null }, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>("the transaction to refund is unknown");
        await Gateway(declined).Invoking(g => g.RefundAsync(request with { PaymentReference = "abc" }, CancellationToken.None))
            .Should().ThrowAsync<PaymentGatewayException>();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>Captures requests and answers with a canned response; no network.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public sealed record Captured(HttpMethod Method, Uri Uri, string? Authorization, string Body, IReadOnlyDictionary<string, string> Headers);

    public List<Captured> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Captured(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            body,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
        return respond(request);
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler? handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        new(handler ?? new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), disposeHandler: false);
}
