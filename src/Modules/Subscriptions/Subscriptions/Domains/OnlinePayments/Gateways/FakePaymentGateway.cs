using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Subscriptions.Options;

namespace Subscriptions.Domains.OnlinePayments.Gateways;

/// <summary>
/// Dev / test provider and the TEMPLATE for real adapters. No network: checkout returns a made-up hosted page URL and
/// webhooks are JSON signed with HMAC-SHA256 over the raw body using <c>Subscriptions:PaymentGateways:Fake:Secret</c>,
/// hex in the <c>X-Fake-Signature</c> header:
/// <code>{ "id": "evt_1", "type": "payment.succeeded|payment.failed|refund.created", "reference": "session id", "amount": 100.00, "currency": "SAR" }</code>
/// (<c>refund.created</c> may add <c>refund_id</c>; the amount is then the amount of that one refund; a success may add
/// <c>payment_reference</c>, the provider payment id remembered for later refunds, and the saved-card capture
/// <c>customer_reference</c> + <c>method_reference</c> (+ <c>card_brand</c>, <c>card_last4</c>, <c>card_exp_month</c>,
/// <c>card_exp_year</c>).)
/// <para>
/// Off-session charges are deterministic by the saved method's reference, so tests need no static state: a reference
/// containing <c>declined</c> is declined, <c>action</c> needs 3-D Secure, <c>transient</c> is a provider outage;
/// anything else succeeds.
/// </para>
/// Never enable it in production (the options validator refuses an enabled gateway without a secret).
/// </summary>
internal sealed class FakePaymentGateway(IOptions<SubscriptionOptions> options) : IPaymentGateway
{
    public const string ProviderName = "Fake";
    public const string SignatureHeader = "X-Fake-Signature";

    public string Name => ProviderName;

    public bool SupportsRefundIdempotency => true;

    public bool SupportsOffSessionCharges => true;

    public Task<OffSessionChargeResult> ChargeSavedMethodAsync(OffSessionChargeRequest request, CancellationToken ct)
    {
        var method = request.MethodReference;
        var result = method.Contains("declined", StringComparison.OrdinalIgnoreCase)
            ? new OffSessionChargeResult(OffSessionChargeStatus.Declined, null, "card_declined")
            : method.Contains("action", StringComparison.OrdinalIgnoreCase)
                ? new OffSessionChargeResult(OffSessionChargeStatus.RequiresAction, null, "authentication_required")
                : method.Contains("transient", StringComparison.OrdinalIgnoreCase)
                    ? new OffSessionChargeResult(OffSessionChargeStatus.Transient, null, "provider_unavailable")
                    : new OffSessionChargeResult(
                        OffSessionChargeStatus.Succeeded, $"fake_pi_{request.IdempotencyKey}");
        return Task.FromResult(result);
    }

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var sessionId = $"fake_{Guid.NewGuid():N}";
        var baseUrl = Settings.BaseUrl ?? "https://pay.fake.example.com";
        return Task.FromResult(new CheckoutSession(sessionId, new Uri($"{baseUrl.TrimEnd('/')}/checkout/{sessionId}")));
    }

    /// <summary>Always succeeds, except for a payment whose reference contains <c>declined</c> (to exercise the failure path).</summary>
    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct) =>
        request.ProviderReference.Contains("declined", StringComparison.OrdinalIgnoreCase)
            ? throw new Exceptions.PaymentGatewayException("The fake provider declined the refund.")
            : Task.FromResult(new RefundResult($"fake_re_{Guid.NewGuid():N}", request.Amount));

    public WebhookParseResult ParseWebhook(WebhookRequest request)
    {
        var rawBody = request.RawBody;
        var headers = request.Headers;
        if (string.IsNullOrEmpty(Settings.Secret) ||
            !headers.TryGetValue(SignatureHeader, out var signature) ||
            !IsValid(Settings.Secret, rawBody, signature))
        {
            return WebhookParseResult.BadSignature();
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString() switch
            {
                "payment.succeeded" => PaymentWebhookEventType.PaymentSucceeded,
                "payment.failed" => PaymentWebhookEventType.PaymentFailed,
                "refund.created" => PaymentWebhookEventType.Refunded,
                _ => PaymentWebhookEventType.Other
            };

            var id = root.GetProperty("id").GetString();
            var reference = root.GetProperty("reference").GetString();
            var currency = root.GetProperty("currency").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(currency))
                return WebhookParseResult.Malformed();

            // Optional: the provider's refund id (else the event id is the refund's idempotency key) and payment id.
            var refundId = type == PaymentWebhookEventType.Refunded && root.TryGetProperty("refund_id", out var r)
                ? r.GetString()
                : null;
            var paymentReference = root.TryGetProperty("payment_reference", out var p) ? p.GetString() : null;
            var saved = root.TryGetProperty("customer_reference", out var customer) &&
                        !string.IsNullOrWhiteSpace(customer.GetString())
                ? new SavedMethodCapture(
                    customer.GetString()!,
                    Text(root, "method_reference"),
                    Text(root, "card_brand"),
                    Text(root, "card_last4"),
                    Number(root, "card_exp_month"),
                    Number(root, "card_exp_year"))
                : null;

            return WebhookParseResult.Ok(new PaymentWebhookEvent(
                id, type, reference, root.GetProperty("amount").GetDecimal(), currency.ToUpperInvariant(),
                refundId, paymentReference, type == PaymentWebhookEventType.PaymentSucceeded ? saved : null));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return WebhookParseResult.Malformed();
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    /// <summary>The signature a provider would send for <paramref name="rawBody"/> (tests and local tooling).</summary>
    public static string Sign(string secret, string rawBody) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)));

    private static bool IsValid(string secret, string rawBody, string signature)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(secret, rawBody));
        var actual = Encoding.UTF8.GetBytes(signature.Trim().ToLower(CultureInfo.InvariantCulture));
        return CryptographicOperations.FixedTimeEquals(expected, actual); // false (no throw) on a length mismatch
    }

    private PaymentGatewayOptions Settings =>
        options.Value.PaymentGateways.TryGetValue(ProviderName, out var settings) ? settings : new PaymentGatewayOptions();
}
