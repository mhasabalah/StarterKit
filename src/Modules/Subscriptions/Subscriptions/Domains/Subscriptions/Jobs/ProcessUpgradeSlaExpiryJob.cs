using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>What the SLA job did with one overdue proration payment.</summary>
public enum UpgradeSlaOutcome
{
    Skipped,
    Reverted,
    ClosedWithoutRevert
}

/// <summary>
/// Hourly upgrade-SLA enforcement (job id <c>process-upgrade-sla-expiry</c>). An immediate upgrade whose proration
/// payment is still UNPAID (Pending, or Rejected with no new proof) after <c>UpgradeSlaHours</c> is reverted:
/// the subscription goes back to the original plan, the payment is closed (Failed) and ONLY the invoice tied to that
/// payment is cancelled with a credit note. A proof that is Submitted (awaiting admin review) is never reverted, and
/// previously paid invoices are never touched.
/// Each payment is one <see cref="IUnitOfWorkRunner"/> unit of work: its domain events are published after its
/// commit, and a conflict on one payment is logged and skipped without aborting the batch.
/// </summary>
[DisableConcurrentExecution(60)]
public class ProcessUpgradeSlaExpiryJob(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SubscriptionOptions> options,
    ILogger<ProcessUpgradeSlaExpiryJob> logger)
{
    public const string RevertReason = "Upgrade reverted: proration not paid within the SLA";

    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ProcessAsync(DateTimeOffset.UtcNow, CancellationToken.None);
    }

    public async Task<IReadOnlyDictionary<Guid, UpgradeSlaOutcome>> ProcessAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddHours(-options.Value.UpgradeSlaHours);

        var overdue = await unitOfWork.RunAsync(async (services, token) =>
            (await services.GetRequiredService<SubscriptionsDbContext>().Payments
                    .AsNoTracking()
                    .Where(p => p.Purpose == PaymentPurpose.Proration &&
                                (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Rejected) &&
                                p.CreatedAt <= cutoff)
                    .OrderBy(p => p.CreatedAt)
                    .Select(p => new { p.Id, p.SubscriptionId })
                    .ToListAsync(token))
                .Select(x => (PaymentId: x.Id, x.SubscriptionId))
                .ToList(), ct);

        var outcomes = new Dictionary<Guid, UpgradeSlaOutcome>();
        foreach (var (paymentId, subscriptionId) in overdue)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                outcomes[paymentId] = await unitOfWork.RunAsync(
                    (services, token) => ProcessPaymentAsync(services, subscriptionId, paymentId, now, cutoff, token), ct);
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() || ex is ConflictException)
            {
                logger.LogWarning(ex, "Upgrade SLA for payment {PaymentId} skipped: {Message}", paymentId, ex.Message);
                outcomes[paymentId] = UpgradeSlaOutcome.Skipped;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Upgrade SLA for payment {PaymentId} failed.", paymentId);
                outcomes[paymentId] = UpgradeSlaOutcome.Skipped;
            }
        }

        return outcomes;
    }

    private async Task<UpgradeSlaOutcome> ProcessPaymentAsync(
        IServiceProvider services,
        Guid subscriptionId,
        Guid paymentId,
        DateTimeOffset now,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var repository = services.GetRequiredService<ISubscriptionRepository>();
        var issuer = services.GetRequiredService<IInvoiceIssuer>();
        var cache = services.GetRequiredService<IDistributedCache>();

        var subscription = await repository.GetByIdAsync(subscriptionId, ct);
        var payment = await repository.GetPaymentAsync(subscriptionId, paymentId, ct);

        // Re-check under the unit of work: a proof may have been submitted or approved since the scan.
        if (subscription is null ||
            payment is null ||
            payment.Status is not (PaymentStatus.Pending or PaymentStatus.Rejected) ||
            payment.CreatedAt > cutoff)
        {
            return UpgradeSlaOutcome.Skipped;
        }

        if (subscription.Status == SubscriptionStatus.PastDue)
        {
            // The renewal already billed the upgraded plan; an admin must decide (reverting would desync that bill).
            logger.LogWarning(
                "Unpaid upgrade proration {PaymentId} on past-due subscription {SubscriptionId} needs admin review.",
                paymentId, subscriptionId);
            return UpgradeSlaOutcome.Skipped;
        }

        PlanSnapshot? original = null;
        if (subscription.Status == SubscriptionStatus.Active)
        {
            var originalPlan = payment.OriginalPlanId is { } planId
                ? await dbContext.Plans.Include(p => p.Features).AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct)
                : null;

            if (originalPlan is null ||
                !string.Equals(originalPlan.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase) ||
                originalPlan.BillingCycleInMonths != subscription.BillingCycleInMonths)
            {
                logger.LogWarning(
                    "Cannot revert subscription {SubscriptionId}: original plan {PlanId} is missing or incompatible.",
                    subscriptionId, payment.OriginalPlanId);
                return UpgradeSlaOutcome.Skipped;
            }

            original = PlanSnapshot.From(originalPlan, PricingPolicy.PlanPriceAt(originalPlan, now));
        }

        SubscriptionInvoice? creditNote = null;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(ct))
        {
            if (original is not null)
            {
                subscription.RevertUpgrade(original, now);
                dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
                    subscription.Id,
                    subscription.SubscriberId,
                    subscription.Status,
                    subscription.Status,
                    options.Value.SystemActorId,
                    "System",
                    RevertReason));
            }

            payment.MarkFailed(RevertReason);

            // Only the invoice of THIS payment; earlier (paid) invoices are never credited here.
            var invoice = await dbContext.Invoices.FirstOrDefaultAsync(
                x => x.PaymentId == payment.Id && x.Type == InvoiceType.Proration, ct);
            if (invoice is not null && CreditNotePolicy.Creditable(invoice.TotalAmount, invoice.CreditedAmount) > 0m)
            {
                creditNote = await issuer.IssueCreditNoteAsync(invoice, null, RevertReason, ct);
            }

            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        if (creditNote is not null)
        {
            issuer.EnqueueDelivery(creditNote.Id);
        }

        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), ct);

        logger.LogInformation(
            "Upgrade SLA expired for payment {PaymentId}; subscription {SubscriptionId} {Action}.",
            paymentId, subscriptionId, original is null ? "closed without revert" : "reverted");

        return original is null ? UpgradeSlaOutcome.ClosedWithoutRevert : UpgradeSlaOutcome.Reverted;
    }
}
