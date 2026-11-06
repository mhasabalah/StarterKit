using Subscriptions.Options;
using System.Text.Json;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Domains.OnlinePayments.Services;

internal enum AutoRetryOutcome
{
    /// <summary>No charge was tried (disabled, no usable saved method, gateway cannot do it).</summary>
    NotAttempted = 0,

    /// <summary>The money was collected; the payment is settled (or queued for settlement by the webhook retry job).</summary>
    Succeeded = 1,

    RequiresAction = 2,
    Declined = 3,
    Transient = 4
}

/// <param name="Reason">Machine key of the reason a charge did not complete (<c>authentication_required</c>, <c>declined</c>, <c>expired</c>, <c>unavailable</c>); null otherwise.</param>
internal sealed record AutoRetryResult(AutoRetryOutcome Outcome, string? Reason = null);

/// <summary>
/// One automatic (off-session) charge attempt on the subscriber's default saved method, for one dunning step. See
/// ADR 0018. The steps, in order, are what make a double charge impossible:
/// <list type="number">
/// <item>a <see cref="PaymentAttempt"/> (unique per payment + step) and the <see cref="OnlinePaymentSession"/> the
/// provider's webhook will find are saved BEFORE the provider call;</item>
/// <item>the provider call carries the idempotency key <c>dunning:{paymentId}:{step}</c>;</item>
/// <item>success settles through the SAME inbox + settlement path as a webhook (a synthetic event, so a real
/// <c>payment_intent.succeeded</c> arriving later is a no-op);</item>
/// <item>RequiresAction / Declined close the session; Transient leaves it open for the webhook.</item>
/// </list>
/// </summary>
internal interface IOffSessionRetryService
{
    Task<AutoRetryResult> TryChargeAsync(
        Subscription subscription, SubscriptionPayment payment, string step, DateTimeOffset now, CancellationToken ct);
}

internal sealed class OffSessionRetryService(
    ISavedPaymentMethodRepository methods,
    IPaymentAttemptRepository attempts,
    IOnlinePaymentSessionRepository sessions,
    IPaymentGatewayRegistry gateways,
    IInboundWebhookProcessor inbox,
    IInboundWebhookEventRepository inboxRows,
    IOptions<SubscriptionOptions> options,
    ILogger<OffSessionRetryService> logger) : IOffSessionRetryService
{
    public async Task<AutoRetryResult> TryChargeAsync(
        Subscription subscription, SubscriptionPayment payment, string step, DateTimeOffset now, CancellationToken ct)
    {
        if (!options.Value.Dunning.AutoRetry.Enabled)
            return new AutoRetryResult(AutoRetryOutcome.NotAttempted);

        var method = await methods.GetDefaultAsync(subscription.SubscriberId, ct);
        if (method is null)
            return new AutoRetryResult(AutoRetryOutcome.NotAttempted);
        if (method.IsExpired(now))
            return new AutoRetryResult(AutoRetryOutcome.NotAttempted, "expired");

        IPaymentGateway gateway;
        try
        {
            gateway = gateways.Get(method.Gateway);
        }
        catch (PaymentGatewayNotFoundException)
        {
            return new AutoRetryResult(AutoRetryOutcome.NotAttempted);
        }

        var amount = payment.OutstandingAmount;
        if (!gateway.SupportsOffSessionCharges || amount <= 0m || !payment.IsAwaitingPayment)
            return new AutoRetryResult(AutoRetryOutcome.NotAttempted);

        var attempt = await attempts.GetAsync(payment.Id, step, ct);
        if (attempt is { Outcome: not (PaymentAttemptOutcome.Started or PaymentAttemptOutcome.Transient) })
            return Map(attempt.Outcome, attempt.FailureCode); // this step was already tried: never charge twice

        var sessionRef = PaymentAttempt.KeyFor(payment.Id, step);
        if (attempt is null)
        {
            attempts.Add(PaymentAttempt.Start(payment, step, method.Id, amount, now));
            await attempts.SaveChangesAsync(ct);
            await sessions.AddAsync(OnlinePaymentSession.Create(payment, gateway.Name, sessionRef, amount, now), ct);
            attempt = await attempts.GetAsync(payment.Id, step, ct)
                ?? throw new InvalidOperationException("Payment attempt vanished after it was saved.");
        }

        OffSessionChargeResult result;
        try
        {
            result = await gateway.ChargeSavedMethodAsync(
                new OffSessionChargeRequest(
                    payment.Id, subscription.Id, amount, payment.Currency, method.ProviderCustomerRef,
                    method.ProviderMethodRef, attempt.IdempotencyKey, sessionRef, $"{payment.Purpose} payment"),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Off-session charge for payment {PaymentId} threw.", payment.Id);
            result = new OffSessionChargeResult(OffSessionChargeStatus.Transient, FailureCode: "provider_unavailable");
        }

        var outcome = result.Status switch
        {
            OffSessionChargeStatus.Succeeded => PaymentAttemptOutcome.Succeeded,
            OffSessionChargeStatus.RequiresAction => PaymentAttemptOutcome.RequiresAction,
            OffSessionChargeStatus.Declined => PaymentAttemptOutcome.Declined,
            _ => PaymentAttemptOutcome.Transient
        };
        attempt.Complete(outcome, result.ProviderReference, result.FailureCode, now);
        await attempts.SaveChangesAsync(ct);

        if (result.Status is OffSessionChargeStatus.RequiresAction or OffSessionChargeStatus.Declined)
        {
            var session = await sessions.GetByProviderReferenceAsync(gateway.Name, sessionRef, ct);
            session?.MarkFailed(now);
            await attempts.SaveChangesAsync(ct);
        }
        else if (result.Status == OffSessionChargeStatus.Succeeded)
        {
            await SettleAsync(gateway, payment, sessionRef, amount, result.ProviderReference, ct);
        }

        return Map(outcome, result.FailureCode);
    }

    private async Task SettleAsync(
        IPaymentGateway gateway, SubscriptionPayment payment, string sessionRef, decimal amount, string? providerReference,
        CancellationToken ct)
    {
        var webhookEvent = new PaymentWebhookEvent(
            $"offsession:{sessionRef}", PaymentWebhookEventType.PaymentSucceeded, sessionRef, amount, payment.Currency,
            PaymentReference: providerReference);
        var body = JsonSerializer.Serialize(new { source = "off_session_charge", reference = sessionRef, providerReference });

        var stored = await inbox.StoreAsync(gateway.Name, webhookEvent, body, ct);
        await inbox.ProcessAsync(stored.Id, CancellationToken.None);

        var row = await inboxRows.FindAsync(gateway.Name, webhookEvent.EventId, ct);
        if (row is not { Status: InboundWebhookStatus.Processed })
        {
            // Money was collected but settlement failed: the failed inbox row is retried by the webhook retry job.
            logger.LogError(
                "Payment {PaymentId} was charged off-session but not settled yet; the webhook retry job will settle it.",
                payment.Id);
        }
    }

    private static AutoRetryResult Map(PaymentAttemptOutcome outcome, string? failureCode) => outcome switch
    {
        PaymentAttemptOutcome.Succeeded => new AutoRetryResult(AutoRetryOutcome.Succeeded),
        PaymentAttemptOutcome.RequiresAction => new AutoRetryResult(AutoRetryOutcome.RequiresAction, "authentication_required"),
        PaymentAttemptOutcome.Declined => new AutoRetryResult(AutoRetryOutcome.Declined, "declined"),
        _ => new AutoRetryResult(AutoRetryOutcome.Transient, "unavailable")
    };
}
