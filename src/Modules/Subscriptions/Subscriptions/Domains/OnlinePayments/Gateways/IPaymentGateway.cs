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
    Uri CancelUrl,
    bool SavePaymentMethod = false,
    string? CustomerReference = null,
    string? SessionReference = null);

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
    string? PaymentReference = null,
    SavedMethodCapture? SavedMethod = null);

/// <summary>
/// What a successful checkout reports about the payment method the subscriber agreed to save for future off-session
/// charges. <paramref name="MethodRef"/> may be null when the provider needs a follow-up call to learn it
/// (<see cref="IPaymentGateway.DescribeSavedMethodAsync"/>).
/// </summary>
public sealed record SavedMethodCapture(
    string CustomerRef,
    string? MethodRef,
    string? Brand = null,
    string? Last4 = null,
    int? ExpMonth = null,
    int? ExpYear = null);

public enum OffSessionChargeStatus
{
    /// <summary>The money was collected.</summary>
    Succeeded = 0,

    /// <summary>The issuer wants the cardholder (3-D Secure): the subscriber must pay through a checkout link.</summary>
    RequiresAction = 1,

    /// <summary>The method was refused (declined, expired, insufficient funds, detached): do not retry this step.</summary>
    Declined = 2,

    /// <summary>A provider / network problem: the outcome is unknown or nothing was charged; the checkout link is the fallback.</summary>
    Transient = 3
}

/// <summary>An off-session charge of a saved method: no cardholder present, so the provider may refuse with RequiresAction.</summary>
/// <param name="IdempotencyKey">Stable per payment + dunning step, so a repeated call can never charge twice.</param>
/// <param name="SessionReference">Our reference for the charge; echoed by the provider's webhook so it can find the session.</param>
public sealed record OffSessionChargeRequest(
    Guid PaymentId,
    Guid SubscriptionId,
    decimal Amount,
    string Currency,
    string CustomerReference,
    string MethodReference,
    string IdempotencyKey,
    string SessionReference,
    string Description);

/// <param name="ProviderReference">The provider's charge / payment-intent id, when it created one.</param>
/// <param name="FailureCode">The provider's decline / authentication code (never shown verbatim to the subscriber).</param>
public sealed record OffSessionChargeResult(
    OffSessionChargeStatus Status,
    string? ProviderReference = null,
    string? FailureCode = null);

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

    /// <summary>
    /// True when the adapter can save a payment method at checkout and charge it later without the cardholder
    /// (<see cref="ChargeSavedMethodAsync"/>). Adapters that cannot (Paymob) keep the default and need no code.
    /// </summary>
    bool SupportsOffSessionCharges => false;

    /// <summary>
    /// Charges a saved method now, off-session, idempotent by <see cref="OffSessionChargeRequest.IdempotencyKey"/>.
    /// Never throws for a refusal: it reports <see cref="OffSessionChargeStatus"/>; a provider outage is
    /// <see cref="OffSessionChargeStatus.Transient"/>.
    /// </summary>
    Task<OffSessionChargeResult> ChargeSavedMethodAsync(OffSessionChargeRequest request, CancellationToken ct) =>
        throw new NotSupportedException($"Gateway '{Name}' does not support off-session charges.");

    /// <summary>Removes a saved method at the provider (best effort: the local row is revoked either way).</summary>
    Task DetachAsync(string customerReference, string methodReference, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Completes a <see cref="SavedMethodCapture"/> whose method / card details the webhook did not carry, from the
    /// provider's payment (Stripe: the PaymentIntent's payment method). Null = nothing usable could be learned.
    /// </summary>
    Task<SavedMethodCapture?> DescribeSavedMethodAsync(
        SavedMethodCapture partial, string? paymentReference, CancellationToken ct) =>
        Task.FromResult<SavedMethodCapture?>(partial.MethodRef is null ? null : partial);
}
