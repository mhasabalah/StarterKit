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
/// <c>checkout.session.expired</c>, <c>refund.created</c>, <c>refund.updated</c>, plus (for automatic dunning retries)
/// <c>payment_intent.succeeded</c> and <c>payment_intent.payment_failed</c>.
/// <para>
/// Authenticity is Stripe's own scheme (<see cref="EventUtility.ConstructEvent(string, string, string, long, DateTime?, bool)"/>:
/// HMAC-SHA256 over <c>timestamp.rawBody</c>, constant-time compare, timestamp tolerance). The verified JSON is then
/// read directly, so a Stripe API-version change cannot break the mapping.
/// </para>
/// <para>
/// Refunds are keyed by the refund id (<c>re_...</c>) of <c>refund.created/updated</c> with status
/// <c>succeeded</c>; <c>charge.refunded</c> is deliberately NOT mapped (it carries only a cumulative total and no refund
/// id, so mapping it next to the refund events would book one refund twice). <c>payment_intent.*</c> events are mapped
/// ONLY for our own off-session charges (metadata <c>source=off_session</c>, which also carries our session
/// reference): a failed card attempt inside a Checkout Session does not end it (the customer can retry; only
/// <c>checkout.session.expired</c> / <c>async_payment_failed</c> do) and its success arrives as the checkout event.
/// </para>
/// <para>
/// Saved cards: when checkout asks to save the method, the session sets <c>setup_future_usage=off_session</c> and
/// reuses / creates the Stripe Customer; <c>checkout.session.completed</c> then reports the customer and
/// <see cref="DescribeSavedMethodAsync"/> reads the card details from the PaymentIntent. Off-session charges are
/// PaymentIntents created with <c>confirm=true, off_session=true</c> and the idempotency key
/// <c>dunning:{paymentId}:{step}</c>.
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

    /// <summary>Metadata marker on the PaymentIntents of our own off-session charges.</summary>
    public const string OffSessionSource = "off_session";

    /// <summary>Failure-code prefix of a refusal that is about the request / method, not the card.</summary>
    public const string InvalidRequestPrefix = "invalid_request:";

    public string Name => ProviderName;

    public bool SupportsRefundIdempotency => true;

    public bool SupportsOffSessionCharges => true;

    public bool SupportsAmount(decimal amount, string currency) => StripeAmounts.IsChargeable(amount, currency);

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

        if (request.SavePaymentMethod)
        {
            // The cardholder is told at checkout that the card is kept for future payments (Stripe shows the mandate).
            create.PaymentIntentData = new Stripe.Checkout.SessionPaymentIntentDataOptions { SetupFutureUsage = "off_session" };
            create.Metadata["save_method"] = "true";
            if (!string.IsNullOrWhiteSpace(request.CustomerReference))
                create.Customer = request.CustomerReference;
            else
                create.CustomerCreation = "always";
        }

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

    public async Task<OffSessionChargeResult> ChargeSavedMethodAsync(OffSessionChargeRequest request, CancellationToken ct)
    {
        var create = new PaymentIntentCreateOptions
        {
            Amount = StripeAmounts.ToMinorUnits(request.Amount, request.Currency),
            Currency = request.Currency.ToLowerInvariant(),
            Customer = request.CustomerReference,
            PaymentMethod = request.MethodReference,
            Confirm = true,
            OffSession = true,
            Description = request.Description,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = OffSessionSource,
                ["session_ref"] = request.IdempotencyKey,
                ["payment_id"] = request.PaymentId.ToString(),
                ["subscription_id"] = request.SubscriptionId.ToString()
            }
        };

        try
        {
            var intent = await new PaymentIntentService(Client()).CreateAsync(
                create, new RequestOptions { IdempotencyKey = request.IdempotencyKey }, ct);
            return intent.Status switch
            {
                "succeeded" => new OffSessionChargeResult(OffSessionChargeStatus.Succeeded, intent.Id),
                "requires_action" or "requires_confirmation" =>
                    new OffSessionChargeResult(OffSessionChargeStatus.RequiresAction, intent.Id, "authentication_required"),
                // processing: the payment_intent.succeeded / payment_failed webhook settles it.
                "processing" => new OffSessionChargeResult(OffSessionChargeStatus.Transient, intent.Id, "processing"),
                _ => new OffSessionChargeResult(OffSessionChargeStatus.Declined, intent.Id, intent.LastPaymentError?.Code ?? intent.Status)
            };
        }
        catch (StripeException ex)
        {
            return ClassifyChargeError(ex);
        }
    }

    internal static OffSessionChargeResult ClassifyChargeError(StripeException ex)
    {
        var error = ex.StripeError;
        var code = error?.Code ?? error?.Type;
        var intentId = error?.PaymentIntent?.Id;

        if (error?.Type == "card_error")
        {
            return error.Code == "authentication_required"
                ? new OffSessionChargeResult(OffSessionChargeStatus.RequiresAction, intentId, error.Code)
                : new OffSessionChargeResult(OffSessionChargeStatus.Declined, intentId, code);
        }

        // api_error / api_connection_error / rate_limit_error / idempotency_error / no body at all (network): nothing
        // is known to have been charged; the idempotency key makes a later repeat safe.
        // invalid_request_error: the request itself is unusable (detached / unknown method, bad amount), not a card
        // decline: a distinct failure code so the subscriber is not told "your card was declined".
        return error?.Type is "invalid_request_error"
            ? new OffSessionChargeResult(OffSessionChargeStatus.Declined, intentId, InvalidRequestPrefix + (error.Code ?? "unknown"))
            : new OffSessionChargeResult(OffSessionChargeStatus.Transient, intentId, code ?? "provider_unavailable");
    }

    public async Task<OffSessionChargeResult> GetChargeStatusAsync(string providerReference, CancellationToken ct)
    {
        try
        {
            var intent = await new PaymentIntentService(Client()).GetAsync(providerReference, cancellationToken: ct);
            return intent.Status switch
            {
                "succeeded" => new OffSessionChargeResult(OffSessionChargeStatus.Succeeded, intent.Id),
                "requires_action" or "requires_confirmation" =>
                    new OffSessionChargeResult(OffSessionChargeStatus.RequiresAction, intent.Id, "authentication_required"),
                "requires_payment_method" or "canceled" =>
                    new OffSessionChargeResult(OffSessionChargeStatus.Declined, intent.Id, intent.LastPaymentError?.Code ?? intent.Status),
                _ => new OffSessionChargeResult(OffSessionChargeStatus.Transient, intent.Id, intent.Status)
            };
        }
        catch (StripeException)
        {
            return new OffSessionChargeResult(OffSessionChargeStatus.Transient, providerReference, "status_unknown");
        }
    }

    public async Task DetachAsync(string customerReference, string methodReference, CancellationToken ct)
    {
        try
        {
            await new PaymentMethodService(Client()).DetachAsync(methodReference, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            throw new PaymentGatewayException($"Stripe refused to detach the payment method: {ex.StripeError?.Message ?? ex.Message}", ex);
        }
    }

    public async Task<SavedMethodCapture?> DescribeSavedMethodAsync(
        SavedMethodCapture partial, string? paymentReference, CancellationToken ct)
    {
        if (partial.MethodRef is not null)
            return partial;
        if (string.IsNullOrWhiteSpace(paymentReference))
            return null;

        try
        {
            var intent = await new PaymentIntentService(Client()).GetAsync(
                paymentReference, new PaymentIntentGetOptions { Expand = ["payment_method"] }, cancellationToken: ct);
            var method = intent.PaymentMethod;
            if (method is null || string.IsNullOrWhiteSpace(method.Id))
                return null;

            return partial with
            {
                MethodRef = method.Id,
                Brand = method.Card?.Brand,
                Last4 = method.Card?.Last4,
                ExpMonth = (int?)method.Card?.ExpMonth,
                ExpYear = (int?)method.Card?.ExpYear
            };
        }
        catch (StripeException)
        {
            return null; // best effort: the card just is not saved; the checkout link stays the fallback
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
                    var customer = OptionalString(obj, "customer");
                    var saveRequested = obj.TryGetProperty("metadata", out var sessionMetadata) &&
                                        OptionalString(sessionMetadata, "save_method") == "true";
                    return WebhookParseResult.Ok(new PaymentWebhookEvent(
                        eventId, PaymentWebhookEventType.PaymentSucceeded, RequiredString(obj, "id"),
                        StripeAmounts.FromMinorUnits(obj.GetProperty("amount_total").GetInt64(), currency),
                        currency.ToUpperInvariant(),
                        PaymentReference: OptionalString(obj, "payment_intent"),
                        SavedMethod: saveRequested && customer is not null ? new SavedMethodCapture(customer, null) : null));
                }

                case "payment_intent.succeeded":
                case "payment_intent.payment_failed":
                {
                    // Only our own off-session charges; checkout payments are settled by their checkout events.
                    if (!obj.TryGetProperty("metadata", out var metadata) ||
                        OptionalString(metadata, "source") != OffSessionSource ||
                        OptionalString(metadata, "session_ref") is not { } sessionRef)
                    {
                        return Other(eventId);
                    }

                    var currency = RequiredString(obj, "currency");
                    var succeeded = type == "payment_intent.succeeded";
                    var minor = obj.GetProperty(succeeded ? "amount_received" : "amount").GetInt64();
                    return WebhookParseResult.Ok(new PaymentWebhookEvent(
                        eventId,
                        succeeded ? PaymentWebhookEventType.PaymentSucceeded : PaymentWebhookEventType.PaymentFailed,
                        sessionRef,
                        StripeAmounts.FromMinorUnits(minor, currency),
                        currency.ToUpperInvariant(),
                        PaymentReference: succeeded ? RequiredString(obj, "id") : null));
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

/// <summary>
/// Stripe minor units, from Stripe's own tables (https://docs.stripe.com/currencies), which differ from ISO 4217 in a
/// few places and are therefore NOT taken from <see cref="Currency"/>:
/// <list type="bullet">
/// <item>Zero-decimal: BIF CLP DJF GNF JPY KMF KRW MGA PYG RWF VND VUV XAF XOF XPF (MGA is two-decimal in ISO, zero here).</item>
/// <item>ISK and UGX: zero-decimal currencies that Stripe's API still takes as TWO-decimal for backwards compatibility,
/// so the amount is sent multiplied by 100 and must be a whole number of units.</item>
/// <item>Three-decimal: BHD JOD KWD OMR. The amount in minor units must be a multiple of 10 (the last digit is 0).
/// IQD, LYD and TND have three decimals in ISO but Stripe does not list them: not chargeable here.</item>
/// <item>HUF and TWD: two-decimal in the API, but Stripe requires whole units (a multiple of 100) for payouts, so only
/// whole-unit amounts are chargeable.</item>
/// </list>
/// </summary>
internal static class StripeAmounts
{
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    private static readonly HashSet<string> LegacyTwoDecimal = new(StringComparer.OrdinalIgnoreCase) { "ISK", "UGX" };

    private static readonly HashSet<string> ThreeDecimal = new(StringComparer.OrdinalIgnoreCase) { "BHD", "JOD", "KWD", "OMR" };

    private static readonly HashSet<string> UnsupportedThreeDecimal = new(StringComparer.OrdinalIgnoreCase) { "IQD", "LYD", "TND" };

    private static readonly HashSet<string> WholeUnitsOnly = new(StringComparer.OrdinalIgnoreCase) { "HUF", "TWD" };

    private static int Exponent(string currency) =>
        ZeroDecimal.Contains(currency) ? 0
        : ThreeDecimal.Contains(currency) || UnsupportedThreeDecimal.Contains(currency) ? 3
        : 2; // includes ISK / UGX (sent x100) and every other currency

    private static decimal Factor(string currency) => (decimal)Math.Pow(10, Exponent(currency));

    public static long ToMinorUnits(decimal amount, string currency) =>
        (long)decimal.Round(amount * Factor(currency), 0, MidpointRounding.AwayFromZero);

    public static decimal FromMinorUnits(long minor, string currency) =>
        decimal.Round(minor / Factor(currency), Exponent(currency), MidpointRounding.AwayFromZero);

    /// <summary>True when Stripe can charge exactly this amount (see the rules above).</summary>
    public static bool IsChargeable(decimal amount, string currency)
    {
        if (UnsupportedThreeDecimal.Contains(currency))
            return false;

        var exact = amount * Factor(currency);
        if (exact != decimal.Truncate(exact))
            return false; // would be rounded: more decimals than Stripe takes for this currency

        var minor = (long)exact;
        if (ThreeDecimal.Contains(currency))
            return minor % 10 == 0;
        if (LegacyTwoDecimal.Contains(currency) || WholeUnitsOnly.Contains(currency))
            return minor % 100 == 0;
        return true;
    }
}
