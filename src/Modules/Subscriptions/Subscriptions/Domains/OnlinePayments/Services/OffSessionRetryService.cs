using System.Text.Json;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.OnlinePayments.Services;

internal enum AutoRetryOutcome
{
    /// <summary>No charge was tried (disabled, no usable saved method, gateway cannot do it, an earlier charge still open).</summary>
    NotAttempted = 0,

    /// <summary>The money was collected; the payment is settled (or queued for settlement by the webhook retry job).</summary>
    Succeeded = 1,

    RequiresAction = 2,
    Declined = 3,
    Transient = 4
}

/// <param name="Reason">
/// Machine key of why the payment was not collected: <c>authentication_required</c>, <c>declined</c>, <c>expired</c>,
/// <c>unavailable</c>, <c>processing</c> (a charge is in flight at the provider) or <c>method_unusable</c> (the provider
/// refused the saved method itself). Null when nothing was tried or the charge succeeded.
/// </param>
/// <param name="ChargeInFlight">
/// A charge may still complete at the provider: do NOT offer a new checkout link (the subscriber could pay twice).
/// </param>
internal sealed record AutoRetryResult(AutoRetryOutcome Outcome, string? Reason = null, bool ChargeInFlight = false);

/// <summary>
/// One automatic (off-session) charge attempt on the subscriber's default saved method, for one dunning step. See
/// ADR 0018. What makes a double charge impossible, also ACROSS steps:
/// <list type="number">
/// <item>before a new charge, every earlier attempt of the payment that is still <c>Started</c> or <c>Transient</c> is
/// reconciled with the provider (looked up by its reference, or replayed with its own idempotency key while the
/// provider still remembers it); a success is settled, a refusal closes it, and a charge that may still complete
/// (or an answer we cannot get) means NO new charge this step;</item>
/// <item>a <see cref="PaymentAttempt"/> (unique per payment + step) and the <see cref="OnlinePaymentSession"/> the
/// provider's webhook will find are saved BEFORE the provider call, which carries the idempotency key
/// <c>dunning:{paymentId}:{step}</c>;</item>
/// <item>success settles through the SAME inbox + settlement path as a webhook (a synthetic event, so a real
/// <c>payment_intent.succeeded</c> arriving later is a no-op); a settlement that is rejected after the money moved
/// raises an admin audit entry for manual reconciliation;</item>
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
    IAdminAuditWriter audit,
    IOptions<SubscriptionOptions> options,
    ILogger<OffSessionRetryService> logger) : IOffSessionRetryService
{
    /// <summary>Stripe remembers an idempotency key for 24 hours; replaying one is only trusted inside this window.</summary>
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromHours(23);

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
        if (!gateway.SupportsOffSessionCharges || amount <= 0m || !payment.IsAwaitingPayment ||
            !gateway.SupportsAmount(amount, payment.Currency))
        {
            return new AutoRetryResult(AutoRetryOutcome.NotAttempted);
        }

        var all = await attempts.GetForPaymentAsync(payment.Id, ct);
        var attempt = all.FirstOrDefault(x => x.Step == step);
        if (attempt is { Outcome: not (PaymentAttemptOutcome.Started or PaymentAttemptOutcome.Transient) })
            return Map(attempt); // this step was already tried: never charge twice

        // Earlier steps whose outcome is unknown must be settled with the provider BEFORE a new charge.
        foreach (var earlier in all.Where(x => x.Step != step &&
                                               x.Outcome is PaymentAttemptOutcome.Started or PaymentAttemptOutcome.Transient))
        {
            switch (await ReconcileAsync(earlier, subscription, payment, gateway, now, ct))
            {
                case Reconciled.Settled:
                    return new AutoRetryResult(AutoRetryOutcome.Succeeded);
                case Reconciled.StillOpen:
                    return new AutoRetryResult(
                        AutoRetryOutcome.NotAttempted, "processing", ChargeInFlight: true);
            }
        }

        var key = PaymentAttempt.KeyFor(payment.Id, step);
        if (attempt is null)
        {
            attempts.Add(PaymentAttempt.Start(payment, step, method.Id, amount, now));
            await attempts.SaveChangesAsync(ct);
            await sessions.AddAsync(OnlinePaymentSession.Create(payment, gateway.Name, key, amount, now), ct);
            attempt = await attempts.GetAsync(payment.Id, step, ct)
                ?? throw new InvalidOperationException("Payment attempt vanished after it was saved.");
        }

        var result = await ChargeAsync(gateway, payment, subscription, method, attempt.IdempotencyKey, amount, ct);

        attempt.Complete(ToOutcome(result.Status), result.ProviderReference, result.FailureCode, now);
        await attempts.SaveChangesAsync(ct);

        if (result.Status is OffSessionChargeStatus.RequiresAction or OffSessionChargeStatus.Declined)
        {
            var session = await sessions.GetByProviderReferenceAsync(gateway.Name, key, ct);
            session?.MarkFailed(now);
            await attempts.SaveChangesAsync(ct);
        }
        else if (result.Status == OffSessionChargeStatus.Succeeded)
        {
            await SettleAsync(gateway, payment, key, amount, result.ProviderReference, ct);
        }

        return Map(attempt);
    }

    private enum Reconciled
    {
        /// <summary>The earlier charge had succeeded and is now settled.</summary>
        Settled,

        /// <summary>The earlier charge failed for good; a new charge is safe.</summary>
        Closed,

        /// <summary>It may still complete, or its state cannot be learned: do not charge again.</summary>
        StillOpen
    }

    private async Task<Reconciled> ReconcileAsync(
        PaymentAttempt earlier,
        Subscription subscription,
        SubscriptionPayment payment,
        IPaymentGateway gateway,
        DateTimeOffset now,
        CancellationToken ct)
    {
        OffSessionChargeResult status;
        if (earlier.ProviderReference is { } reference)
        {
            status = await gateway.GetChargeStatusAsync(reference, ct);
        }
        else if (now - earlier.CreatedAt < ReplayWindow &&
                 earlier.SavedPaymentMethodId is { } methodId &&
                 await methods.GetAsync(subscription.SubscriberId, methodId, ct) is { } earlierMethod)
        {
            // No reference came back (the call died): the same key returns the provider's original answer.
            status = await ChargeAsync(
                gateway, payment, subscription, earlierMethod, earlier.IdempotencyKey, earlier.Amount, ct);
        }
        else
        {
            return Reconciled.StillOpen;
        }

        switch (status.Status)
        {
            case OffSessionChargeStatus.Succeeded:
                earlier.Complete(PaymentAttemptOutcome.Succeeded, status.ProviderReference ?? earlier.ProviderReference, null, now);
                await attempts.SaveChangesAsync(ct);
                await SettleAsync(gateway, payment, earlier.IdempotencyKey, earlier.Amount, earlier.ProviderReference, ct);
                return Reconciled.Settled;

            case OffSessionChargeStatus.RequiresAction or OffSessionChargeStatus.Declined:
                earlier.Complete(ToOutcome(status.Status), status.ProviderReference ?? earlier.ProviderReference, status.FailureCode, now);
                (await sessions.GetByProviderReferenceAsync(gateway.Name, earlier.IdempotencyKey, ct))?.MarkFailed(now);
                await attempts.SaveChangesAsync(ct);
                return Reconciled.Closed;

            default:
                if (status.ProviderReference is not null && earlier.ProviderReference is null)
                {
                    earlier.Complete(PaymentAttemptOutcome.Transient, status.ProviderReference, status.FailureCode, now);
                    await attempts.SaveChangesAsync(ct);
                }

                return Reconciled.StillOpen;
        }
    }

    private async Task<OffSessionChargeResult> ChargeAsync(
        IPaymentGateway gateway,
        SubscriptionPayment payment,
        Subscription subscription,
        SavedPaymentMethod method,
        string idempotencyKey,
        decimal amount,
        CancellationToken ct)
    {
        try
        {
            return await gateway.ChargeSavedMethodAsync(
                new OffSessionChargeRequest(
                    payment.Id, subscription.Id, amount, payment.Currency, method.ProviderCustomerRef,
                    method.ProviderMethodRef, idempotencyKey, $"{payment.Purpose} payment"),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Off-session charge for payment {PaymentId} threw.", payment.Id);
            return new OffSessionChargeResult(OffSessionChargeStatus.Transient, FailureCode: "provider_unavailable");
        }
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
        if (row is { Status: InboundWebhookStatus.Processed })
            return;

        // The money moved but settlement did not happen (the payment changed meanwhile, the subscription is no longer
        // in a settleable state, ...). A permanent rejection is never retried by the job, so a human must reconcile:
        // settle the payment manually or refund the charge in the provider dashboard. The audit entry is that alert.
        logger.LogError(
            "Payment {PaymentId} was charged off-session ({Gateway} {ProviderReference}) but settlement failed; reconcile manually.",
            payment.Id, gateway.Name, providerReference);
        audit.Record(
            "OffSessionChargeUnsettled",
            payment.Id,
            "Payment",
            subscriberId: default,
            reason: $"Charged off-session at {gateway.Name} (reference {providerReference ?? "unknown"}, inbox event {webhookEvent.EventId}) " +
                    $"but settlement failed: {row?.Status.ToString() ?? "no inbox row"}. Settle the payment manually or refund the charge.",
            amount: amount,
            currency: payment.Currency,
            after: new { payment.Id, payment.SubscriptionId, Gateway = gateway.Name, ProviderReference = providerReference });
        await attempts.SaveChangesAsync(ct);
    }

    private static PaymentAttemptOutcome ToOutcome(OffSessionChargeStatus status) => status switch
    {
        OffSessionChargeStatus.Succeeded => PaymentAttemptOutcome.Succeeded,
        OffSessionChargeStatus.RequiresAction => PaymentAttemptOutcome.RequiresAction,
        OffSessionChargeStatus.Declined => PaymentAttemptOutcome.Declined,
        _ => PaymentAttemptOutcome.Transient
    };

    private static AutoRetryResult Map(PaymentAttempt attempt) => attempt.Outcome switch
    {
        PaymentAttemptOutcome.Succeeded => new AutoRetryResult(AutoRetryOutcome.Succeeded),
        PaymentAttemptOutcome.RequiresAction => new AutoRetryResult(AutoRetryOutcome.RequiresAction, "authentication_required"),
        PaymentAttemptOutcome.Declined when attempt.FailureCode?.StartsWith(StripePaymentGateway.InvalidRequestPrefix, StringComparison.Ordinal) == true =>
            new AutoRetryResult(AutoRetryOutcome.Declined, "method_unusable"),
        PaymentAttemptOutcome.Declined => new AutoRetryResult(AutoRetryOutcome.Declined, "declined"),
        // A transient answer WITH a provider reference may still complete: no new checkout link.
        _ when attempt.ProviderReference is not null => new AutoRetryResult(AutoRetryOutcome.Transient, "processing", ChargeInFlight: true),
        _ => new AutoRetryResult(AutoRetryOutcome.Transient, "unavailable")
    };
}
