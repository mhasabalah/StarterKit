using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Options;

namespace Subscriptions.Domains.OnlinePayments.Gateways;

/// <summary>
/// Paymob (Accept) adapter: Intention API checkout (unified checkout), transaction processed-callback, refund API.
/// Configuration (<c>Subscriptions:PaymentGateways:Paymob</c>): <c>ApiKey</c> = secret key (<c>Authorization: Token ...</c>),
/// <c>PublicKey</c> (unified checkout URL), <c>IntegrationIds</c> (payment methods offered), <c>Secret</c> = the HMAC
/// secret of the dashboard, optional <c>BaseUrl</c> (default <c>https://accept.paymob.com</c>) and <c>NotificationUrl</c>.
/// Point the integrations' "Transaction processed callback" at <c>/api/v1/webhooks/payments/Paymob</c>.
/// <para>
/// <b>Authenticity.</b> Paymob does not sign the body: it sends <c>?hmac=</c>, the hex HMAC-SHA512 (key = HMAC secret)
/// of 20 transaction fields concatenated in lexicographic key order (<see cref="HmacFields"/>). Only those fields are
/// signed, so ONLY those fields are trusted: the session is found by <c>order.id</c> (the intention's order id, which
/// is our session id), the amount/currency/transaction id come from signed fields, and unsigned ones
/// (<c>parent_transaction</c>, ...) are never read. Compare is constant-time.
/// </para>
/// <para>
/// Paymob has no event id: the event id is the transaction id plus its state flags, so a pending callback never
/// shadows the final one of the same transaction. A refund is the callback of a transaction with a parent that has
/// <c>is_refunded</c>; its transaction id is the provider refund id (the refund API returns the same id).
/// </para>
/// </summary>
internal sealed class PaymobPaymentGateway(
    IOptions<SubscriptionOptions> options,
    IHttpClientFactory httpClientFactory) : IPaymentGateway
{
    public const string ProviderName = "Paymob";
    public const string HttpClientName = "paymob";
    public const string DefaultBaseUrl = "https://accept.paymob.com";
    public const string HmacQueryKey = "hmac";

    /// <summary>The documented concatenation order of the transaction callback HMAC (dotted = nested object).</summary>
    internal static readonly string[] HmacFields =
    [
        "amount_cents", "created_at", "currency", "error_occured", "has_parent_transaction", "id", "integration_id",
        "is_3d_secure", "is_auth", "is_capture", "is_refunded", "is_standalone_payment", "is_voided", "order.id",
        "owner", "pending", "source_data.pan", "source_data.sub_type", "source_data.type", "success"
    ];

    public string Name => ProviderName;

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var settings = Settings;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.PublicKey) ||
            settings.IntegrationIds.Length == 0)
        {
            throw new PaymentGatewayException("Paymob is not configured (ApiKey, PublicKey and IntegrationIds are required).");
        }

        var cents = Currency.ToMinorUnits(request.Amount, request.Currency);
        var name = string.IsNullOrWhiteSpace(request.Description) ? "Subscription payment" : request.Description;
        var body = new Dictionary<string, object?>
        {
            ["amount"] = cents,
            ["currency"] = request.Currency.ToUpperInvariant(),
            ["payment_methods"] = settings.IntegrationIds,
            ["items"] = new[] { new { name, amount = cents, description = name, quantity = 1 } },
            // Required by Paymob; the hosted page collects the real details.
            ["billing_data"] = new
            {
                first_name = "NA",
                last_name = "NA",
                email = "na@example.com",
                phone_number = "NA",
                apartment = "NA",
                floor = "NA",
                street = "NA",
                building = "NA",
                city = "NA",
                country = "NA",
                state = "NA"
            },
            ["special_reference"] = $"{request.PaymentId:N}-{Guid.NewGuid():N}"[..48],
            ["redirection_url"] = request.SuccessUrl.ToString(),
            ["extras"] = new { payment_id = request.PaymentId, subscription_id = request.SubscriptionId }
        };
        if (!string.IsNullOrWhiteSpace(settings.NotificationUrl))
            body["notification_url"] = settings.NotificationUrl;

        using var message = new HttpRequestMessage(HttpMethod.Post, Url("v1/intention/"))
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Token", settings.ApiKey);

        using var document = await SendAsync(message, "checkout", ct);
        var root = document.RootElement;
        var clientSecret = StringOrNumber(root, "client_secret");
        var orderId = StringOrNumber(root, "intention_order_id");
        if (string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(orderId))
            throw new PaymentGatewayException("Paymob returned an intention without a client secret / order id.");

        var redirect = $"{BaseUrl}/unifiedcheckout/?publicKey={Uri.EscapeDataString(settings.PublicKey)}" +
                       $"&clientSecret={Uri.EscapeDataString(clientSecret)}";
        return new CheckoutSession(orderId, new Uri(redirect));
    }

    public async Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Settings.ApiKey))
            throw new PaymentGatewayException("Paymob is not configured (missing ApiKey).");
        if (!long.TryParse(request.PaymentReference, NumberStyles.None, CultureInfo.InvariantCulture, out var transactionId))
            throw new PaymentGatewayException("The Paymob transaction of this checkout is unknown; refund it in the Paymob dashboard.");

        var cents = Currency.ToMinorUnits(request.Amount, request.Currency);
        using var message = new HttpRequestMessage(HttpMethod.Post, Url("api/acceptance/void_refund/refund"))
        {
            Content = JsonContent.Create(new { transaction_id = transactionId, amount_cents = cents })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Token", Settings.ApiKey);

        using var document = await SendAsync(message, "refund", ct);
        var root = document.RootElement;
        // Paymob answers 200 with the refund transaction; success=false means it was declined.
        if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            throw new PaymentGatewayException("Paymob declined the refund.");

        var refundId = StringOrNumber(root, "id");
        if (string.IsNullOrWhiteSpace(refundId))
            throw new PaymentGatewayException("Paymob returned a refund without an id.");

        return new RefundResult(refundId, request.Amount);
    }

    public WebhookParseResult ParseWebhook(WebhookRequest request)
    {
        var secret = Settings.Secret;
        if (string.IsNullOrEmpty(secret) ||
            !request.Query.TryGetValue(HmacQueryKey, out var received) || string.IsNullOrWhiteSpace(received))
        {
            return WebhookParseResult.BadSignature();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request.RawBody);
        }
        catch (JsonException)
        {
            return WebhookParseResult.Malformed(); // not JSON at all: nothing to verify, nothing stored
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("obj", out var obj) || obj.ValueKind != JsonValueKind.Object)
                return WebhookParseResult.Malformed();

            if (!IsValid(secret, obj, received))
                return WebhookParseResult.BadSignature();

            return Map(obj);
        }
    }

    /// <summary>The hex HMAC-SHA512 Paymob would send for <paramref name="obj"/> (tests and local tooling).</summary>
    internal static string Sign(string secret, JsonElement obj)
    {
        var data = new StringBuilder();
        foreach (var path in HmacFields)
        {
            data.Append(FieldValue(obj, path));
        }

        return Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data.ToString())));
    }

    private static bool IsValid(string secret, JsonElement obj, string received)
    {
        var expected = Encoding.ASCII.GetBytes(Sign(secret, obj));
        var actual = Encoding.UTF8.GetBytes(received.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual); // false (no throw) on a length mismatch
    }

    private static WebhookParseResult Map(JsonElement obj)
    {
        try
        {
            var id = FieldValue(obj, "id");
            var orderId = FieldValue(obj, "order.id");
            var currency = FieldValue(obj, "currency");
            if (id.Length == 0 || orderId.Length == 0 || currency.Length == 0)
                return WebhookParseResult.Malformed();

            var cents = obj.GetProperty("amount_cents").GetInt64();
            var amount = Currency.FromMinorUnits(cents, currency); // minor units of THIS currency (100 / 1000 / 1)

            var success = Flag(obj, "success");
            var pending = Flag(obj, "pending");
            var isRefunded = Flag(obj, "is_refunded");
            var isVoided = Flag(obj, "is_voided");
            var hasParent = Flag(obj, "has_parent_transaction");
            var isAuthOnly = Flag(obj, "is_auth") && !Flag(obj, "is_capture");

            // Paymob has no event id: the transaction id + its state is the idempotency key.
            var eventId = $"txn:{id}:{(success ? 1 : 0)}{(pending ? 1 : 0)}{(isRefunded ? 1 : 0)}{(isVoided ? 1 : 0)}{(hasParent ? 1 : 0)}";
            currency = currency.ToUpperInvariant();

            PaymentWebhookEvent Event(PaymentWebhookEventType type, string? refundId = null, string? paymentReference = null) =>
                new(eventId, type, orderId, amount, currency, refundId, paymentReference);

            if (pending || isVoided || isAuthOnly)
                return WebhookParseResult.Ok(Event(PaymentWebhookEventType.Other));

            if (hasParent)
            {
                // A child transaction: a successful refund, or an unrelated capture / void.
                return WebhookParseResult.Ok(success && isRefunded
                    ? Event(PaymentWebhookEventType.Refunded, refundId: id)
                    : Event(PaymentWebhookEventType.Other));
            }

            if (isRefunded)
                return WebhookParseResult.Ok(Event(PaymentWebhookEventType.Other)); // the parent updated after a refund

            return WebhookParseResult.Ok(success
                ? Event(PaymentWebhookEventType.PaymentSucceeded, paymentReference: id)
                : Event(PaymentWebhookEventType.PaymentFailed));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return WebhookParseResult.Malformed();
        }
    }

    /// <summary>A signed field as it enters the HMAC string: numbers as-is, booleans lower-case, missing/null empty.</summary>
    private static string FieldValue(JsonElement obj, string path)
    {
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return string.Empty;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static bool Flag(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? StringOrNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage message, string operation, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(message, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new PaymentGatewayException($"Paymob refused the {operation} (HTTP {(int)response.StatusCode}).");
            return JsonDocument.Parse(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       && !ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException($"Paymob {operation} failed: {ex.Message}", ex);
        }
    }

    private string BaseUrl => (Settings.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');

    private string Url(string path) => $"{BaseUrl}/{path}";

    private PaymentGatewayOptions Settings =>
        options.Value.PaymentGateways.TryGetValue(ProviderName, out var settings) ? settings : new PaymentGatewayOptions();
}
