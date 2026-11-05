using System.Globalization;
using System.Text.Json;
using Stripe;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Options;

namespace Subscriptions.Domains.OnlinePayments.Gateways;

/// <summary>
/// Stripe adapter (Checkout Sessions + Refunds API). Configuration (<c>Subscriptions:PaymentGateways:Stripe</c>):
/// <c>ApiKey</c> = secret key (<c>sk_...</c>), <c>Secret</c> = webhook endpoint signing secret (<c>whsec_...</c>),
/// <c>WebhookToleranceSeconds</c> (default 300, the replay window of the signed timestamp). Webhook URL:
/// <c>/api/v1/webhooks/payments/Stripe</c>; subscribe to <c>checkout.session.completed</c>,
/// <c>checkout.session.async_payment_succeeded</c>, <c>checkout.session.async_payment_failed</c>,
/// <c>checkout.session.expired</c>, <c>refund.created</c>, <c>refund.updated</c>.
/// <para>
/// Authenticity is Stripe's own scheme (<see cref="EventUtility.ConstructEvent(string, string, string, long, DateTime?, bool)"/>:
/// HMAC-SHA256 over <c>timestamp.rawBody</c>, constant-time compare, timestamp tolerance). The verified JSON is then
/// read directly, so a Stripe API-version change cannot break the mapping.
/// </para>
/// <para>
/// Refunds are keyed by the refund id (<c>re_...</c>) of <c>refund.created/updated</c> with status
/// <c>succeeded</c>; <c>charge.refunded</c> is deliberately NOT mapped (it carries only a cumulative total and no refund
/// id, so mapping it next to the refund events would book one refund twice). <c>payment_intent.payment_failed</c> is not
/// mapped either: a failed card attempt does not end a Checkout Session (the customer can retry); only
/// <c>checkout.session.expired</c> / <c>async_payment_failed</c> do.
/// </para>
/// </summary>
internal sealed class StripePaymentGateway(
    IOptions<SubscriptionOptions> options,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider) : IPaymentGateway
{
    public const string ProviderName = "Stripe";
    public const string HttpClientName = "stripe";
    public const string SignatureHeader = "Stripe-Signature";

    public string Name => ProviderName;

    public bool SupportsRefundIdempotency => true;

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var currency = request.Currency.ToUpperInvariant();
        var create = new Stripe.Checkout.SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = request.PaymentId.ToString(),
            SuccessUrl = request.SuccessUrl.ToString(),
            CancelUrl = request.CancelUrl.ToString(),
            LineItems =
            [
                new Stripe.Checkout.SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new Stripe.Checkout.SessionLineItemPriceDataOptions
                    {
                        Currency = currency.ToLowerInvariant(),
                        UnitAmount = StripeAmounts.ToMinorUnits(request.Amount, currency),
                        ProductData = new Stripe.Checkout.SessionLineItemPriceDataProductDataOptions
                        {
                            Name = string.IsNullOrWhiteSpace(request.Description) ? "Subscription payment" : request.Description
                        }
                    }
                }
            ],
            Metadata = new Dictionary<string, string>
            {
                ["payment_id"] = request.PaymentId.ToString(),
                ["subscription_id"] = request.SubscriptionId.ToString()
            }
        };

        try
        {
            var session = await new Stripe.Checkout.SessionService(Client()).CreateAsync(create, cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(session.Id) || !Uri.TryCreate(session.Url, UriKind.Absolute, out var url))
                throw new PaymentGatewayException("Stripe returned a checkout session without a hosted URL.");
            return new CheckoutSession(session.Id, url);
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException($"Stripe refused the checkout: {ex.StripeError?.Message ?? ex.Message}", ex);
        }
    }

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct)
    {
        // Stripe refunds a PaymentIntent (remembered from checkout.session.completed).
        if (string.IsNullOrWhiteSpace(request.PaymentReference))
            throw new PaymentGatewayException("The Stripe payment intent of this checkout is unknown; refund it in the Stripe dashboard.");

        var create = new RefundCreateOptions
        {
            PaymentIntent = request.PaymentReference,
            Amount = StripeAmounts.ToMinorUnits(request.Amount, request.Currency),
            Reason = "requested_by_customer"
        };

        try
        {
            var refund = await new RefundService(Client()).CreateAsync(
                create, new RequestOptions { IdempotencyKey = request.IdempotencyKey }, ct);
            return new RefundResult(refund.Id, StripeAmounts.FromMinorUnits(refund.Amount, request.Currency));
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException($"Stripe refused the refund: {ex.StripeError?.Message ?? ex.Message}", ex);
        }
    }

    public WebhookParseResult ParseWebhook(WebhookRequest request)
    {
        var settings = Settings;
        if (string.IsNullOrEmpty(settings.Secret) ||
            !request.Headers.TryGetValue(SignatureHeader, out var signature) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return WebhookParseResult.BadSignature();
        }

        try
        {
            // Signature + replay window in one call. Stripe uses a constant-time compare and throws for a wrong
            // signature, a malformed header and a timestamp outside the tolerance alike.
            EventUtility.ValidateSignature(
                request.RawBody, signature, settings.Secret,
                Math.Max(1, settings.WebhookToleranceSeconds), timeProvider.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (StripeException)
        {
            return WebhookParseResult.BadSignature();
        }

        return Map(request.RawBody);
    }

    /// <summary>Maps an ALREADY VERIFIED event body.</summary>
    internal static WebhookParseResult Map(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var eventId = root.GetProperty("id").GetString();
            var type = root.GetProperty("type").GetString();
            var obj = root.GetProperty("data").GetProperty("object");
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(type))
                return WebhookParseResult.Malformed();

            switch (type)
            {
                case "checkout.session.completed":
                case "checkout.session.async_payment_succeeded":
                {
                    // A completed session of a delayed method is not paid yet: wait for async_payment_succeeded.
                    if (obj.GetProperty("payment_status").GetString() != "paid")
                        return Other(eventId);

                    var currency = RequiredString(obj, "currency");
                    return WebhookParseResult.Ok(new PaymentWebhookEvent(
                        eventId, PaymentWebhookEventType.PaymentSucceeded, RequiredString(obj, "id"),
                        StripeAmounts.FromMinorUnits(obj.GetProperty("amount_total").GetInt64(), currency),
                        currency.ToUpperInvariant(),
                        PaymentReference: OptionalString(obj, "payment_intent")));
                }

                case "checkout.session.async_payment_failed":
                case "checkout.session.expired":
                {
                    var currency = OptionalString(obj, "currency") ?? "XXX";
                    var amount = obj.TryGetProperty("amount_total", out var total) && total.ValueKind == JsonValueKind.Number
                        ? StripeAmounts.FromMinorUnits(total.GetInt64(), currency)
                        : 0m;
                    return WebhookParseResult.Ok(new PaymentWebhookEvent(
                        eventId, PaymentWebhookEventType.PaymentFailed, RequiredString(obj, "id"), amount,
                        currency.ToUpperInvariant()));
                }

                case "refund.created":
                case "refund.updated":
                {
                    if (OptionalString(obj, "status") != "succeeded")
                        return Other(eventId); // pending / failed / canceled: refund.updated reports the final state

                    var currency = RequiredString(obj, "currency");
                    var refundId = RequiredString(obj, "id");
                    return WebhookParseResult.Ok(new PaymentWebhookEvent(
                        eventId, PaymentWebhookEventType.Refunded, RequiredString(obj, "payment_intent"),
                        StripeAmounts.FromMinorUnits(obj.GetProperty("amount").GetInt64(), currency),
                        currency.ToUpperInvariant(), ProviderRefundId: refundId));
                }

                default:
                    return Other(eventId);
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
                                       or FormatException or ArgumentException)
        {
            return WebhookParseResult.Malformed();
        }
    }

    private static WebhookParseResult Other(string eventId) =>
        WebhookParseResult.Ok(new PaymentWebhookEvent(eventId, PaymentWebhookEventType.Other, "n/a", 0m, "XXX"));

    private static string RequiredString(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        return string.IsNullOrWhiteSpace(value) ? throw new FormatException($"'{name}' is empty.") : value;
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private StripeClient Client()
    {
        var apiKey = Settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new PaymentGatewayException("Stripe is not configured (missing ApiKey).");

        return new StripeClient(new StripeClientOptions
        {
            ApiKey = apiKey,
            HttpClient = new SystemNetHttpClient(httpClientFactory.CreateClient(HttpClientName), maxNetworkRetries: 0)
        });
    }

    private PaymentGatewayOptions Settings =>
        options.Value.PaymentGateways.TryGetValue(ProviderName, out var settings) ? settings : new PaymentGatewayOptions();
}

/// <summary>Stripe minor units: 2 decimals by default, 0 for the zero-decimal currencies, 3 for the three-decimal ones.</summary>
internal static class StripeAmounts
{
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND"
    };

    private static int Exponent(string currency) =>
        ZeroDecimal.Contains(currency) ? 0 : ThreeDecimal.Contains(currency) ? 3 : 2;

    private static decimal Factor(string currency) =>
        ZeroDecimal.Contains(currency) ? 1m : ThreeDecimal.Contains(currency) ? 1000m : 100m;

    public static long ToMinorUnits(decimal amount, string currency) =>
        (long)decimal.Round(amount * Factor(currency), 0, MidpointRounding.AwayFromZero);

    public static decimal FromMinorUnits(long minor, string currency) =>
        decimal.Round(minor / Factor(currency), Exponent(currency), MidpointRounding.AwayFromZero);
}
