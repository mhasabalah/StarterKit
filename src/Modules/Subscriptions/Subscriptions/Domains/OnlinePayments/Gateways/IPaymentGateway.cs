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
/// <param name="ProviderReference">The checkout session id the event is about.</param>
public sealed record PaymentWebhookEvent(
    string EventId,
    PaymentWebhookEventType Type,
    string ProviderReference,
    decimal Amount,
    string Currency);

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
    /// Verifies the signature over the RAW body (never a re-serialized one) in constant time, then normalizes the
    /// payload. Must not throw for bad input: return <see cref="WebhookParseResult.BadSignature"/> /
    /// <see cref="WebhookParseResult.Malformed"/>.
    /// </summary>
    WebhookParseResult ParseWebhook(string rawBody, IReadOnlyDictionary<string, string> headers);
}
