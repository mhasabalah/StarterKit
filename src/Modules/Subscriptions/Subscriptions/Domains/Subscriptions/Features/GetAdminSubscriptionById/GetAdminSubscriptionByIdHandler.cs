using Subscriptions.Domains.Ledger;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.GetAdminSubscriptionById;

public class GetAdminSubscriptionByIdQueryValidator : AbstractValidator<GetAdminSubscriptionByIdQuery>
{
    public GetAdminSubscriptionByIdQueryValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
    }
}

/// <summary>
/// Subscriber-360: the subscription with its payments (incl. receipts, proof hash, duplicate flag), invoices, credit
/// notes, refunds, transition log, scheduled changes, ledger (+ totals per currency) and plan limits. One no-tracking
/// query per collection (no cartesian explosion).
/// </summary>
public class GetAdminSubscriptionByIdHandler(SubscriptionsDbContext dbContext, ISender sender)
    : IQueryHandler<GetAdminSubscriptionByIdQuery, AdminSubscriptionDetailsDto>
{
    public async Task<AdminSubscriptionDetailsDto> Handle(GetAdminSubscriptionByIdQuery query, CancellationToken cancellationToken)
    {
        var id = query.SubscriptionId;

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AdminSubscriptionDetailDto(
                x.Id,
                x.SubscriberId,
                x.SubscriberDisplayName,
                x.SubscriberEmail,
                x.PlanId,
                x.PlanName,
                x.Status.ToString(),
                x.Price,
                x.Currency,
                x.BillingCycleInMonths,
                x.MaxItems,
                x.MaxStorageGb,
                x.MaxUsers,
                x.StartDate,
                x.CurrentPeriodStart,
                x.RenewalDate,
                x.GracePeriodEndsAt,
                x.EndDate,
                x.CancelledAt,
                x.CancelledAtPeriodEnd,
                x.ScheduledCancellationDate,
                x.CancellationReason.HasValue ? x.CancellationReason.Value.ToString() : null,
                x.CancellationNote,
                x.DeferredProrationAmount,
                x.ComplimentaryUntil,
                x.UpdatedAt,
                x.Version))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new SubscriptionNotFoundException(id);

        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.SubscriptionId == id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new AdminSubscriptionPaymentDto(
                p.Id,
                p.Purpose.ToString(),
                p.Status.ToString(),
                p.Amount,
                p.AmountReceived,
                p.Amount - p.AmountReceived > 0m ? p.Amount - p.AmountReceived : 0m,
                p.Currency,
                new PaymentMethodSnapshotDto(
                    p.PaymentMethodId,
                    p.PaymentMethodType,
                    p.PaymentMethodDisplayName,
                    p.PaymentMethodValue,
                    p.PaymentMethodDescription),
                p.ProofAttachmentId,
                p.ProofContentHash,
                p.DuplicateProof,
                p.DuplicateOfPaymentId,
                p.CreatedAt,
                p.SubmittedAt,
                p.PaidAt,
                p.ReviewedBy,
                p.ReviewedAt,
                p.Notes,
                p.Version,
                p.Receipts
                    .OrderBy(r => r.ReceivedAt)
                    .Select(r => new PaymentReceiptDto(
                        r.Id, r.Amount, r.Currency, r.Method, r.Reference, r.ReceivedAt, r.Note, r.RecordedBy, r.RecordedAt))
                    .ToList()))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var documents = await dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.SubscriptionId == id)
            .OrderByDescending(i => i.IssuedAt)
            .Select(AdminBillingProjections.ToAdminInvoiceDto)
            .ToListAsync(cancellationToken);

        var refunds = await dbContext.RefundRequests
            .AsNoTracking()
            .Where(r => r.SubscriptionId == id)
            .OrderByDescending(r => r.RequestedAt)
            .Select(AdminBillingProjections.ToAdminRefundRequestDto)
            .ToListAsync(cancellationToken);

        var transitions = await dbContext.TransitionLogs
            .AsNoTracking()
            .Where(t => t.SubscriptionId == id)
            .OrderBy(t => t.OccurredAt)
            .Select(t => new SubscriptionTransitionDto(
                t.FromStatus.HasValue ? t.FromStatus.Value.ToString() : null,
                t.ToStatus.ToString(),
                t.ActorId,
                t.ActorType,
                t.Reason,
                t.OccurredAt))
            .ToListAsync(cancellationToken);

        var changes = await dbContext.ScheduledPlanChanges
            .AsNoTracking()
            .Where(c => c.SubscriptionId == id)
            .OrderByDescending(c => c.ScheduledAt)
            .Select(c => new AdminScheduledPlanChangeDto(
                c.Id,
                c.ChangeType.ToString(),
                c.Status.ToString(),
                c.NewPlanId,
                c.NewPlanName,
                c.NewPrice,
                c.ScheduledAt,
                c.EffectiveAt,
                c.AppliedAt))
            .ToListAsync(cancellationToken);

        var ledger = await dbContext.LedgerEntries
            .AsNoTracking()
            .Where(l => l.SubscriptionId == id)
            .OrderByDescending(l => l.OccurredAt)
            .Select(AdminBillingProjections.ToLedgerEntryDto)
            .ToListAsync(cancellationToken);

        var totals = BillingDashboardCalculator.Revenue(ledger.Select(l => new LedgerAmountGroup(
            l.Currency, Enum.Parse<LedgerEntryType>(l.Type), l.PlanId, l.PlanName, l.PaymentMethod, l.Amount)));

        var limits = await sender.Send(new GetActivePlanLimitsQuery(subscription.SubscriberId), cancellationToken);

        const string creditNote = nameof(InvoiceType.CreditNote);
        return new AdminSubscriptionDetailsDto(
            subscription,
            payments,
            documents.Where(d => d.Type != creditNote).ToList(),
            documents.Where(d => d.Type == creditNote).ToList(),
            refunds,
            transitions,
            changes,
            ledger,
            totals,
            limits);
    }
}
