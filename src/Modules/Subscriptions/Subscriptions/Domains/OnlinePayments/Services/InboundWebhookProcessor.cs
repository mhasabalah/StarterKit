using Subscriptions.Data.Repositories;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.OnlinePayments.Services;

/// <summary>
/// Stores verified provider webhooks in the inbox and applies them. Settlement goes through
/// <see cref="IPaymentSettlementService"/> (the same path as a manual receipt that completes a payment), so an online
/// payment books the activation/renewal, invoice and ledger entry exactly like an offline one.
/// </summary>
internal interface IInboundWebhookProcessor
{
    /// <summary>
    /// Insert-or-ignore by <c>(provider, event_id)</c>. Returns the row id, whether the event was already stored
    /// (<c>Duplicate</c>) and the status of the stored row (Pending for a new one).
    /// </summary>
    Task<StoredWebhook> StoreAsync(string provider, PaymentWebhookEvent webhookEvent, string rawBody, CancellationToken ct);

    /// <summary>
    /// Applies one stored event in its own transaction. Never throws for a processing failure: the failure is recorded
    /// on the row (retried by the job unless permanent). Already processed rows are skipped.
    /// </summary>
    Task ProcessAsync(Guid inboxId, CancellationToken ct);
}

/// <param name="Duplicate">True when the event id was already stored.</param>
/// <param name="Status">Status of the stored row at lookup time (Pending when new).</param>
internal sealed record StoredWebhook(Guid Id, bool Duplicate, InboundWebhookStatus Status);

internal sealed class InboundWebhookProcessor(
    IInboundWebhookEventRepository inbox,
    IOnlinePaymentSessionRepository sessions,
    ISubscriptionRepository subscriptionRepository,
    IPaymentSettlementService settlement,
    IOnlinePaymentRefundRepository onlineRefunds,
    IRefundBooking refundBooking,
    IInvoiceIssuer invoiceIssuer,
    ILedgerRepository ledger,
    IAdminAuditWriter audit,
    ISavedMethodRecorder savedMethodRecorder,
    IOptions<SubscriptionOptions> options,
    TimeProvider timeProvider,
    ILogger<InboundWebhookProcessor> logger) : IInboundWebhookProcessor
{
    public async Task<StoredWebhook> StoreAsync(
        string provider,
        PaymentWebhookEvent webhookEvent,
        string rawBody,
        CancellationToken ct)
    {
        var id = Guid.NewGuid();
        if (await inbox.TryInsertAsync(id, provider, webhookEvent, rawBody, timeProvider.GetUtcNow(), ct))
            return new StoredWebhook(id, Duplicate: false, InboundWebhookStatus.Pending);

        var existing = await inbox.FindAsync(provider, webhookEvent.EventId, ct)
            ?? throw new InvalidOperationException("Webhook event vanished between conflict and lookup.");
        return new StoredWebhook(existing.Id, Duplicate: true, existing.Status);
    }

    public async Task ProcessAsync(Guid inboxId, CancellationToken ct)
    {
        var maxAttempts = options.Value.PaymentWebhooks.MaxAttempts;
        var now = timeProvider.GetUtcNow();

        Subscription? subscription = null;
        SettlementResult? result = null;
        IReadOnlyList<SubscriptionInvoice> creditNotes = [];
        string? savedMethodJson = null;
        var provider = string.Empty;
        string? paymentReference = null;

        await using (var transaction = await inbox.BeginTransactionAsync(ct))
        {
            var inbound = await inbox.GetByIdAsync(inboxId, ct);
            if (inbound is null || inbound.Status == InboundWebhookStatus.Processed)
                return;

            try
            {
                (subscription, result, creditNotes) = await ApplyAsync(inbound, now, ct);
                savedMethodJson = inbound.SavedMethod;
                provider = inbound.Provider;
                paymentReference = inbound.PaymentReference;
                inbound.MarkProcessed(now);
                await inbox.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await RecordFailureAsync(inboxId, ex, now, maxAttempts, ct);
                return;
            }
        }

        if (subscription is not null && result is not null)
        {
            await settlement.AfterCommitAsync(subscription, result, ct);
        }

        // The subscriber agreed to save the card at checkout: store it now that the payment is safely settled.
        if (subscription is not null && result is not null && savedMethodJson is not null &&
            System.Text.Json.JsonSerializer.Deserialize<SavedMethodCapture>(savedMethodJson) is { } capture)
        {
            await savedMethodRecorder.RecordAsync(provider, subscription.SubscriberId, capture, paymentReference, ct);
        }

        foreach (var note in creditNotes)
        {
            invoiceIssuer.EnqueueDelivery(note.Id);
        }
    }

    private async Task<(Subscription? Subscription, SettlementResult? Result, IReadOnlyList<SubscriptionInvoice> CreditNotes)> ApplyAsync(
        InboundWebhookEvent inbound,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var type = Enum.Parse<PaymentWebhookEventType>(inbound.Type);
        if (type == PaymentWebhookEventType.Other)
            return (null, null, []);

        // A refund event may only know the provider payment id (remembered from the success event).
        var session = (type == PaymentWebhookEventType.Refunded
                ? await sessions.GetByAnyReferenceAsync(inbound.Provider, inbound.ProviderReference!, ct)
                : await sessions.GetByProviderReferenceAsync(inbound.Provider, inbound.ProviderReference!, ct))
            ?? throw new WebhookRejectedException($"Unknown payment reference '{inbound.ProviderReference}'.");

        switch (type)
        {
            case PaymentWebhookEventType.PaymentFailed:
                session.MarkFailed(now);
                return (null, null, []);

            case PaymentWebhookEventType.Refunded:
                return (null, null, await BookRefundAsync(inbound, session, now, ct));

            default:
                var (subscription, result) = await SettleAsync(inbound, session, now, ct);
                return (subscription, result, []);
        }
    }

    /// <summary>
    /// A refund the provider reports (dashboard refund, or the echo of one we issued): one credit-note set and ONE
    /// Refund ledger entry per provider refund id, never more than the session received.
    /// </summary>
    private async Task<IReadOnlyList<SubscriptionInvoice>> BookRefundAsync(
        InboundWebhookEvent inbound,
        OnlinePaymentSession session,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Same refund reported again (new event id, or the echo of an admin refund): booked once, ever.
        var refundId = inbound.ProviderRefundId ?? inbound.EventId;
        if (await onlineRefunds.FindAsync(inbound.Provider, refundId, ct) is not null)
        {
            logger.LogInformation("Refund {Provider}/{RefundId} is already booked; ignoring.", inbound.Provider, refundId);
            return [];
        }

        if (session.Status is not (OnlinePaymentSessionStatus.Succeeded or OnlinePaymentSessionStatus.Refunded))
            throw new WebhookRejectedException("Refund reported for a payment that was never settled.");

        var amount = inbound.Amount ?? 0m;
        if (amount <= 0m)
            throw new WebhookRejectedException("The refund amount must be positive.");
        if (!string.Equals(inbound.Currency, session.Currency, StringComparison.OrdinalIgnoreCase))
            throw new WebhookRejectedException(
                $"Currency mismatch: the payment was {session.Currency}, the refund is {inbound.Currency}.");

        // Serialize concurrent refunds of one session so they cannot jointly exceed what was received.
        await onlineRefunds.LockSessionAsync(session.Id, ct);
        var alreadyRefunded = await onlineRefunds.SumForSessionAsync(session.Id, ct);
        if (alreadyRefunded + amount > session.Amount)
            throw new WebhookRejectedException(
                $"Refund of {CurrencyRules.FormatWithCode(amount, session.Currency)} exceeds the money received: {CurrencyRules.Format(session.Amount, session.Currency)} paid, {CurrencyRules.Format(alreadyRefunded, session.Currency)} already refunded.");

        var subscription = await subscriptionRepository.GetByIdAsync(session.SubscriptionId, ct)
            ?? throw new WebhookRejectedException("The subscription no longer exists.");

        var allocation = await refundBooking.IssueCreditNotesAsync(
            subscription.Id, amount, $"Refund {inbound.Provider} {refundId}", ct);

        var actor = options.Value.SystemActorId;
        ledger.Add(LedgerEntry.ForProviderRefund(
            subscription, session.PaymentId, amount, session.Currency, allocation.Notes.FirstOrDefault()?.Id,
            actor, now, inbound.Provider, refundId));
        onlineRefunds.Add(OnlinePaymentRefund.Create(session, refundId, amount, null, now, bookedByWebhook: true));

        if (alreadyRefunded + amount == session.Amount)
            session.MarkRefunded(now);

        audit.Record(
            "ProviderRefund",
            session.PaymentId,
            "Payment",
            subscription.SubscriberId,
            $"{inbound.Provider} refund {refundId} (event {inbound.EventId})",
            amount,
            session.Currency,
            before: new { Refunded = alreadyRefunded },
            after: new
            {
                Refunded = alreadyRefunded + amount,
                CreditNotes = allocation.Notes.Select(x => new { x.Id, x.InvoiceNumber, x.TotalAmount }).ToList(),
                UncreditedAmount = allocation.Uncredited
            },
            adminId: actor);

        return allocation.Notes;
    }

    private async Task<(Subscription? Subscription, SettlementResult? Result)> SettleAsync(
        InboundWebhookEvent inbound,
        OnlinePaymentSession session,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Another event already settled this very session (provider re-sent it under a new event id): nothing to do.
        if (session.Status == OnlinePaymentSessionStatus.Succeeded)
        {
            session.RecordPaymentReference(inbound.PaymentReference);
            return (null, null);
        }

        var amount = inbound.Amount ?? 0m;
        if (amount != session.Amount ||
            !string.Equals(inbound.Currency, session.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new WebhookRejectedException(
                $"Amount mismatch: expected {CurrencyRules.FormatWithCode(session.Amount, session.Currency)}, provider reported {CurrencyRules.FormatWithCode(amount, inbound.Currency)}.");
        }

        var subscription = await subscriptionRepository.GetByIdAsync(session.SubscriptionId, ct)
            ?? throw new WebhookRejectedException("The subscription no longer exists.");
        var payment = await subscriptionRepository.GetPaymentAsync(subscription.Id, session.PaymentId, ct)
            ?? throw new WebhookRejectedException("The payment no longer exists.");

        if (!payment.IsAwaitingPayment)
            throw new WebhookRejectedException(
                $"Payment is already {payment.Status}; the money received must be reconciled manually (refund).");

        if (payment.OutstandingAmount != amount)
            throw new WebhookRejectedException(
                $"Amount mismatch: {CurrencyRules.FormatWithCode(payment.OutstandingAmount, payment.Currency)} is outstanding, provider reported {CurrencyRules.Format(amount, payment.Currency)}.");

        settlement.EnsureSettleable(subscription, payment);

        var actor = options.Value.SystemActorId;
        var statusBefore = subscription.Status;
        var (_, completed) = payment.RecordReceipt(
            amount, inbound.Provider, inbound.ProviderReference, now, $"Online payment (event {inbound.EventId})",
            actor, subscription.SubscriberId, now);
        if (!completed)
            throw new WebhookRejectedException("The payment was not completed by the reported amount.");

        var result = await settlement.SettleAsync(
            subscription, payment, amount, actor, now, inbound.Provider, inbound.ProviderReference, ct);
        subscription.Touch(now);
        session.MarkSucceeded(now, inbound.PaymentReference);

        audit.Record(
            "SettleOnlinePayment",
            payment.Id,
            "Payment",
            subscription.SubscriberId,
            $"{inbound.Provider} event {inbound.EventId}",
            amount,
            payment.Currency,
            before: new { SubscriptionStatus = statusBefore.ToString() },
            after: new
            {
                SubscriptionStatus = subscription.Status.ToString(),
                PaymentStatus = payment.Status.ToString(),
                InvoiceId = result.Invoice.Id,
                result.Invoice.InvoiceNumber
            },
            adminId: actor);

        return (subscription, result);
    }

    private async Task RecordFailureAsync(
        Guid inboxId,
        Exception exception,
        DateTimeOffset now,
        int maxAttempts,
        CancellationToken ct)
    {
        // Deterministic business refusals never succeed on retry; anything else (concurrency, DB hiccup) might.
        var permanent = exception is WebhookRejectedException or CustomException;

        inbox.ClearTracking();
        var inbound = await inbox.GetByIdAsync(inboxId, ct) ?? throw new InvalidOperationException("Inbox row vanished.");
        // A concurrent delivery may have processed it meanwhile: never regress Processed to Failed.
        if (inbound.Status == InboundWebhookStatus.Processed)
            return;

        inbound.MarkFailed(exception.Message, now, maxAttempts, permanent);
        await inbox.SaveChangesAsync(ct);

        if (permanent)
            logger.LogWarning("Webhook {Provider}/{EventId} rejected: {Reason}", inbound.Provider, inbound.EventId, exception.Message);
        else
            logger.LogError(exception, "Webhook {Provider}/{EventId} failed (attempt {Attempt}).",
                inbound.Provider, inbound.EventId, inbound.Attempts);
    }
}
