using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Data.Repositories;
using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.MarkRefundProcessed;

public class MarkRefundProcessedCommandValidator : AbstractValidator<MarkRefundProcessedCommand>
{
    public MarkRefundProcessedCommandValidator()
    {
        RuleFor(x => x.RefundRequestId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.ExternalTransactionRef).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// Marks an approved refund as paid out and issues credit notes for the refunded amount against the subscription's
/// invoices (newest first, each up to its creditable remainder), and writes the Refund ledger entry, all in one
/// transaction.
/// <para>
/// Money that was received ONLINE is returned through the payment provider first (<see cref="IPaymentGateway.RefundAsync"/>,
/// newest settled session first, never more than a session received); the provider refund ids replace the admin's
/// reference and are recorded, so the provider's own refund webhook is recognized and not booked a second time. Any
/// remainder (manual payments) is the admin's off-line transfer, exactly as before.
/// </para>
/// </summary>
internal sealed class MarkRefundProcessedHandler(
    SubscriptionsDbContext dbContext,
    IRefundBooking refundBooking,
    IInvoiceIssuer invoiceIssuer,
    ILedgerRepository ledger,
    IAdminAuditWriter audit,
    IOnlinePaymentRefundRepository onlineRefunds,
    IPaymentGatewayRegistry gateways,
    TimeProvider timeProvider)
    : ICommandHandler<MarkRefundProcessedCommand, Unit>
{
    public async Task<Unit> Handle(MarkRefundProcessedCommand command, CancellationToken cancellationToken)
    {
        var refundRequest = await dbContext.RefundRequests
            .FirstOrDefaultAsync(x => x.Id == command.RefundRequestId, cancellationToken)
            ?? throw new NotFoundException($"Refund request {command.RefundRequestId} not found.");

        // The version is compared up front as well (ExpectVersion alone only fails at the first save), so a stale
        // request is refused BEFORE any money moves at the provider.
        if (command.Version is not null && command.Version != refundRequest.Version)
            throw new ConcurrencyConflictException();
        dbContext.ExpectVersion(refundRequest, command.Version);
        if (refundRequest.Status != RefundStatus.Approved)
            throw new ConflictException("Only approved refunds can be marked as processed.");

        // Provider call BEFORE the books are touched; each refund is saved at once so a later failure or crash can
        // never lose the fact that the money already left (the retry subtracts it and reuses the idempotency key).
        await RefundOnlineAsync(refundRequest, cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var providerRefundIds = await onlineRefunds.GetProviderRefundIdsForRequestAsync(refundRequest.Id, cancellationToken);
        var externalRef = providerRefundIds.Count > 0
            ? string.Join(",", providerRefundIds)
            : command.ExternalTransactionRef;
        if (externalRef.Length > 200)
            externalRef = externalRef[..200];

        refundRequest.MarkProcessed(command.AdminId, externalRef);

        // Money already booked by the provider's own webhook (an admin refund echoed before we got here) is not booked twice.
        var bookedByWebhook = await onlineRefunds.SumBookedByWebhookForRequestAsync(refundRequest.Id, cancellationToken);
        var toBook = Math.Max(0m, refundRequest.RequestedAmount - bookedByWebhook);

        var allocation = new CreditNoteAllocation([], toBook);
        if (toBook > 0m)
        {
            allocation = await refundBooking.IssueCreditNotesAsync(
                refundRequest.SubscriptionId, toBook, $"Refund {externalRef}", cancellationToken);

            // Money left: ONE Refund entry. The credit notes above document it and are not booked again in the ledger.
            ledger.Add(LedgerEntry.ForRefund(
                refundRequest, allocation.Notes.FirstOrDefault()?.Id, command.AdminId, timeProvider.GetUtcNow(), toBook));
        }

        audit.Record(
            "ProcessRefund",
            refundRequest.Id,
            "RefundRequest",
            refundRequest.SubscriberId,
            externalRef,
            refundRequest.RequestedAmount,
            refundRequest.Currency,
            before: new { Status = RefundStatus.Approved.ToString() },
            after: new
            {
                Status = refundRequest.Status.ToString(),
                CreditNotes = allocation.Notes.Select(x => new { x.Id, x.InvoiceNumber, x.TotalAmount }).ToList(),
                UncreditedAmount = allocation.Uncredited,
                ProviderRefunds = providerRefundIds
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var note in allocation.Notes)
        {
            invoiceIssuer.EnqueueDelivery(note.Id);
        }

        return Unit.Value;
    }

    /// <summary>How long a started attempt is presumed to still be running before a retry may look at it again.</summary>
    internal static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

    private async Task RefundOnlineAsync(SubscriptionRefundRequest refundRequest, CancellationToken ct)
    {
        var wasStarted = refundRequest.ProcessingStartedAt is not null;
        if (refundRequest.ProcessingStartedAt is { } startedAt)
        {
            if (timeProvider.GetUtcNow() - startedAt < ProcessingLease)
                throw new ConflictException("This refund is already being processed.");

            // A previous attempt got an unknown outcome. The provider's own webhook may already have booked its money
            // (unlinked): adopt it instead of refunding the same money again.
            var remaining = refundRequest.RequestedAmount - await onlineRefunds.SumForRequestAsync(refundRequest.Id, ct);
            var adopted = false;
            foreach (var candidate in await onlineRefunds.GetUnlinkedWebhookRefundsSinceAsync(
                         refundRequest.SubscriptionId, startedAt, ct))
            {
                if (candidate.Amount > remaining)
                    continue;

                candidate.LinkToRefundRequest(refundRequest.Id);
                remaining -= candidate.Amount;
                adopted = true;
            }

            if (adopted)
                await dbContext.SaveChangesAsync(ct);
        }

        var target = refundRequest.RequestedAmount - await onlineRefunds.SumForRequestAsync(refundRequest.Id, ct);
        if (target <= 0m)
            return;

        var claimed = false;
        var sessions = await onlineRefunds.GetSettledSessionsAsync(refundRequest.SubscriptionId, ct);
        foreach (var session in sessions)
        {
            if (target <= 0m)
                break;

            var refundedOnSession = await onlineRefunds.SumForSessionAsync(session.Id, ct);
            var amount = Math.Min(target, session.Amount - refundedOnSession);
            if (amount <= 0m)
                continue;

            var gateway = gateways.Get(session.Provider);
            if (wasStarted && !gateway.SupportsRefundIdempotency)
            {
                // The provider cannot dedup a repeated call and an earlier attempt may have refunded already.
                throw new ConflictException(
                    "A refund for this request is pending at the provider; awaiting webhook confirmation. " +
                    "It is not sent again.");
            }

            if (!claimed)
            {
                // Committed BEFORE the provider is called: a concurrent call fails here (stale version -> 409) or
                // sees the marker, and a crash afterwards can never lead to a blind second refund.
                refundRequest.BeginProcessing(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(ct);
                claimed = true;
            }

            RefundResult result;
            try
            {
                result = await gateway.RefundAsync(
                    new RefundRequest(
                        session.ProviderReference,
                        session.ProviderPaymentReference,
                        amount,
                        session.Currency,
                        $"refund-{refundRequest.Id:N}-{session.Id:N}", // stable: never embeds amounts or progress
                        $"Refund request {refundRequest.Id}"),
                    ct);
            }
            catch (PaymentGatewayException) when (gateway.SupportsRefundIdempotency)
            {
                // Safe to retry later (the provider dedups by key): release the claim.
                refundRequest.ClearProcessing(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            var now = timeProvider.GetUtcNow();
            var existing = await onlineRefunds.FindAsync(session.Provider, result.RefundId, ct);
            if (existing is null)
                onlineRefunds.Add(OnlinePaymentRefund.Create(session, result.RefundId, result.Amount, refundRequest.Id, now));
            else
                existing.LinkToRefundRequest(refundRequest.Id); // the provider's webhook got here first and booked it

            await dbContext.SaveChangesAsync(ct);
            target -= result.Amount;
        }
    }
}
