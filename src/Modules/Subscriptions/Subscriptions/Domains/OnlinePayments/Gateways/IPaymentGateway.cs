using Subscriptions.Domains.OnlinePayments.Exceptions;

namespace Subscriptions.Domains.OnlinePayments.Gateways;

/// <summary>What the provider must charge: the outstanding amount of one pending payment.</summary>
/// <param name="PaymentId">Our payment id; sent to the provider as its client reference.</param>
public sealed record CheckoutRequest(
    Guid PaymentId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    string Description,
    Uri SuccessUrl,
    Uri CancelUrl);

/// <summary>The hosted checkout created at the provider.</summary>
/// <param name="SessionId">The provider's session / charge id; webhooks refer to it.</param>
/// <param name="RedirectUrl">Where to send the subscriber to pay.</param>
public sealed record CheckoutSession(string SessionId, Uri RedirectUrl);

public enum PaymentWebhookEventType
{
    PaymentSucceeded = 0,
    PaymentFailed = 1,
    Refunded = 2,

    /// <summary>A verified event this system has no use for (stored, then marked processed).</summary>
    Other = 3
}

/// <summary>A verified, provider-neutral webhook event.</summary>
/// <param name="EventId">The provider's unique event id (the idempotency key together with the provider name).</param>
/// <param name="ProviderReference">
/// What the event is about: the checkout session id returned by <see cref="IPaymentGateway.CreateCheckoutAsync"/>, or,
/// for a <see cref="PaymentWebhookEventType.Refunded"/> event, whatever the provider reports the refund against (the
/// checkout session id, or the payment id remembered from the success event via <paramref name="PaymentReference"/>).
/// </param>
/// <param name="Amount">The money amount of THIS event: the payment amount, or the amount of this one refund.</param>
/// <param name="ProviderRefundId">The provider's refund id (refund events only): the idempotency key of a refund.</param>
/// <param name="PaymentReference">
/// The provider's payment / charge / transaction id, when the event reports one. A success event's value is stored on
/// the session so a later refund event (which may only know this id) and an API refund can find the money.
/// </param>
public sealed record PaymentWebhookEvent(
    string EventId,
    PaymentWebhookEventType Type,
    string ProviderReference,
    decimal Amount,
    string Currency,
    string? ProviderRefundId = null,
    string? PaymentReference = null);

/// <summary>A provider call exactly as received: raw body (the signature covers these bytes), headers and query string.</summary>
/// <param name="Query">Query-string values; Paymob, for one, signs with an <c>hmac</c> query parameter.</param>
public sealed record WebhookRequest(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query)
{
    public WebhookRequest(string rawBody, IReadOnlyDictionary<string, string> headers)
        : this(rawBody, headers, new Dictionary<string, string>())
    {
    }
}

/// <summary>Money to give back for one settled online payment.</summary>
/// <param name="ProviderReference">The checkout session id.</param>
/// <param name="PaymentReference">The provider payment / charge / transaction id remembered from the success event, if any.</param>
/// <param name="IdempotencyKey">Stable per logical refund so a retry after a crash can never refund twice (where the provider supports it).</param>
public sealed record RefundRequest(
    string ProviderReference,
    string? PaymentReference,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string? Reason = null);

/// <param name="RefundId">The provider's refund id; a later refund webhook carries the same id, so it is recognized.</param>
public sealed record RefundResult(string RefundId, decimal Amount);

public enum WebhookFailure
{
    None = 0,
    InvalidSignature = 1,
    MalformedPayload = 2
}

/// <summary>Outcome of <see cref="IPaymentGateway.ParseWebhook"/>: a verified event, or why it was refused.</summary>
public sealed record WebhookParseResult
{
    public PaymentWebhookEvent? Event { get; private init; }
    public WebhookFailure Failure { get; private init; }

    public bool IsValid => Event is not null;

    public static WebhookParseResult Ok(PaymentWebhookEvent webhookEvent) => new() { Event = webhookEvent };

    /// <summary>The signature is missing or does not match: 401, nothing stored.</summary>
    public static WebhookParseResult BadSignature() => new() { Failure = WebhookFailure.InvalidSignature };

    /// <summary>The signature is fine but the body is not a usable event: 400, nothing stored.</summary>
    public static WebhookParseResult Malformed() => new() { Failure = WebhookFailure.MalformedPayload };
}

/// <summary>
/// Port to an online payment provider (Stripe, Paymob, Tap, Moyasar, ...). One adapter per provider, registered with
/// <c>services.AddScoped&lt;IPaymentGateway, MyGateway&gt;()</c> and switched on by
/// <c>Subscriptions:PaymentGateways:&lt;Name&gt;:Enabled</c>. The adapter owns everything provider-specific
/// (API calls, signature scheme, payload shape); settlement stays provider-neutral.
/// </summary>
internal interface IPaymentGateway
{
    /// <summary>The route/config name, e.g. <c>Fake</c>. Compared case-insensitively.</summary>
    string Name { get; }

    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);

    /// <summary>
    /// Verifies the signature over the RAW body (never a re-serialized one; a provider that signs fields instead signs
    /// the verified fields) in constant time, rejects stale timestamps where the scheme has one, then normalizes the
    /// payload. Must not throw for bad input: return <see cref="WebhookParseResult.BadSignature"/> /
    /// <see cref="WebhookParseResult.Malformed"/>.
    /// </summary>
    WebhookParseResult ParseWebhook(WebhookRequest request);

    /// <summary>
    /// Refunds <see cref="RefundRequest.Amount"/> of a settled payment at the provider.
    /// </summary>
    /// <exception cref="PaymentGatewayException">The provider refused or could not be reached.</exception>
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct);

    /// <summary>
    /// True when the provider dedups refunds by <see cref="RefundRequest.IdempotencyKey"/>, so repeating a refund call
    /// after an unknown outcome is safe. When false (Paymob), an unknown outcome must be settled by the provider's webhook.
    /// </summary>
    bool SupportsRefundIdempotency => false;
}
