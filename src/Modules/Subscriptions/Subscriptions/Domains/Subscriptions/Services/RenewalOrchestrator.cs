using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>What happened to one subscription at its renewal boundary.</summary>
public enum RenewalBoundaryOutcome
{
    Skipped,
    Cancelled,
    BilledAfterDowngrade,
    Billed,

    /// <summary>A free trial ended: the first paid period was billed (PastDue, with the post-trial grace).</summary>
    TrialEnded
}

/// <summary>
/// The ONE place the renewal boundary is processed (it replaces three racing jobs). For each due subscription, in
/// order of renewal date and each in its own scope/unit of work:
/// <list type="number">
/// <item>apply a scheduled (end-of-period) cancellation, and stop;</item>
/// <item>apply a scheduled downgrade;</item>
/// <item>if still Active and due: mark PastDue and create the renewal payment at the EFFECTIVE price (plan price
/// change, discount, plan after downgrade) plus any deferred upgrade proration, less an active coupon.</item>
/// </list>
/// A TRIALING subscription whose trial ended is billed the same way for its first paid period
/// (<see cref="RenewalBoundaryOutcome.TrialEnded"/>): paying converts it to Active through the normal renewal
/// approval, otherwise it expires after <c>Subscriptions:Trials:PostTrialGraceDays</c>.
/// Expiry is separate (<see cref="ProcessExpiriesAsync"/>) and never expires a subscription whose renewal proof is
/// under review (Submitted).
/// <para>
/// Every item runs through <see cref="IUnitOfWorkRunner"/>: a fresh scope, and the item's domain events
/// (SubscriptionExpiredEvent, SubscriptionCancelledEvent, ...) are published after its commit. A concurrency conflict
/// or unique violation on one item is logged and skipped; it never aborts the batch.
/// </para>
/// </summary>
public interface IRenewalOrchestrator
{
    Task<IReadOnlyDictionary<Guid, RenewalBoundaryOutcome>> ProcessDueRenewalsAsync(DateTimeOffset now, CancellationToken ct = default);

    Task<RenewalBoundaryOutcome> ProcessBoundaryAsync(Guid subscriptionId, DateTimeOffset now, CancellationToken ct = default);

    Task<int> ProcessExpiriesAsync(DateTimeOffset now, CancellationToken ct = default);
}

public sealed class RenewalOrchestrator(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SubscriptionOptions> options,
    ILogger<RenewalOrchestrator> logger)
    : IRenewalOrchestrator
{
    public async Task<IReadOnlyDictionary<Guid, RenewalBoundaryOutcome>> ProcessDueRenewalsAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var dueIds = await unitOfWork.RunAsync((services, token) =>
            services.GetRequiredService<SubscriptionsDbContext>().Subscriptions
                .AsNoTracking()
                .Where(x => (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Trialing) && x.RenewalDate <= now)
                .OrderBy(x => x.RenewalDate)
                .Select(x => x.Id)
                .ToListAsync(token), ct);

        var outcomes = new Dictionary<Guid, RenewalBoundaryOutcome>();
        foreach (var id in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                outcomes[id] = await ProcessBoundaryAsync(id, now, ct);
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() || ex is ConflictException)
            {
                // Another request changed it first (e.g. admin cancel); the next run re-evaluates.
                logger.LogWarning(ex, "Renewal boundary for subscription {SubscriptionId} skipped: {Message}", id, ex.Message);
                outcomes[id] = RenewalBoundaryOutcome.Skipped;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Renewal boundary for subscription {SubscriptionId} failed.", id);
                outcomes[id] = RenewalBoundaryOutcome.Skipped;
            }
        }

        return outcomes;
    }

    // Fresh unit of work per subscription: no tracked entities or stale events leak between items, and the
    // subscription's events are published after its commit.
    public Task<RenewalBoundaryOutcome> ProcessBoundaryAsync(Guid subscriptionId, DateTimeOffset now, CancellationToken ct = default) =>
        unitOfWork.RunAsync((services, token) => ProcessBoundaryAsync(services, subscriptionId, now, token), ct);

    private async Task<RenewalBoundaryOutcome> ProcessBoundaryAsync(
        IServiceProvider services,
        Guid subscriptionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var repository = services.GetRequiredService<ISubscriptionRepository>();
        var cache = services.GetRequiredService<IDistributedCache>();
        var settings = options.Value;

        var subscription = await repository.GetByIdAsync(subscriptionId, ct);
        if (subscription is null ||
            subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trialing) ||
            subscription.RenewalDate > now)
        {
            return RenewalBoundaryOutcome.Skipped;
        }

        if (subscription.Status == SubscriptionStatus.Trialing)
        {
            return await EndTrialAsync(dbContext, cache, subscription, now, ct);
        }

        // 1. End-of-period cancellation wins over billing: the subscriber asked not to renew.
        if (subscription.CancelledAtPeriodEnd)
        {
            subscription.ApplyScheduledCancellation(settings.SystemActorId, now);
            LogTransition(dbContext, subscription, SubscriptionStatus.Active, "Scheduled cancellation applied at period end");
            await dbContext.SaveChangesAsync(ct);
            await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), ct);
            logger.LogInformation("Applied scheduled cancellation for subscription {SubscriptionId}.", subscription.Id);
            return RenewalBoundaryOutcome.Cancelled;
        }

        // 2. Downgrade BEFORE pricing, so the renewal bills the new plan.
        var downgraded = false;
        if (subscription.PendingPlanChange is { ChangeType: PlanChangeType.Downgrade } change && change.EffectiveAt <= now)
        {
            subscription.ApplyScheduledDowngrade(now);
            LogTransition(dbContext, subscription, SubscriptionStatus.Active, $"Scheduled downgrade applied to plan {subscription.PlanName}");
            downgraded = true;
        }

        // 3. Bill the next period at the effective price (+ deferred proration).
        var plan = await dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, ct);
        var periodPrice = PricingPolicy.RenewalPeriodPrice(subscription, plan, subscription.RenewalDate!.Value);
        var charge = PricingPolicy.RenewalCharge(
            periodPrice, subscription.DeferredProrationAmount, settings.Tax.ToPolicy(), subscription.ActiveCoupon,
            subscription.Currency);

        var payment = subscription.MarkPastDue(charge, settings.GracePeriodDays, now);
        dbContext.Payments.Add(payment);
        LogTransition(dbContext, subscription, SubscriptionStatus.Active, "Renewal date elapsed");

        await dbContext.SaveChangesAsync(ct);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), ct);

        logger.LogInformation(
            "Subscription {SubscriptionId} past due; renewal payment {PaymentId} for {Amount} {Currency} (deferred proration {Deferred}).",
            subscription.Id, payment.Id, payment.Amount, payment.Currency, charge.DeferredProration);

        return downgraded ? RenewalBoundaryOutcome.BilledAfterDowngrade : RenewalBoundaryOutcome.Billed;
    }

    // The trial is over: bill the first paid period at the effective price (less an active coupon).
    private async Task<RenewalBoundaryOutcome> EndTrialAsync(
        SubscriptionsDbContext dbContext,
        IDistributedCache cache,
        Subscription subscription,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var settings = options.Value;
        var plan = await dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, ct);
        var periodPrice = PricingPolicy.RenewalPeriodPrice(subscription, plan, subscription.RenewalDate!.Value);
        var charge = PricingPolicy.RenewalCharge(
            periodPrice, 0m, settings.Tax.ToPolicy(), subscription.ActiveCoupon, subscription.Currency);

        var payment = subscription.EndTrial(charge, settings.Trials.PostTrialGraceDays, now);
        dbContext.Payments.Add(payment);
        LogTransition(dbContext, subscription, SubscriptionStatus.Trialing, "Trial ended; first paid period billed");

        await dbContext.SaveChangesAsync(ct);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), ct);

        logger.LogInformation(
            "Trial of subscription {SubscriptionId} ended; payment {PaymentId} for {Amount} {Currency} (coupon discount {Discount}).",
            subscription.Id, payment.Id, payment.Amount, payment.Currency, charge.CouponDiscount);

        return RenewalBoundaryOutcome.TrialEnded;
    }

    public async Task<int> ProcessExpiriesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var dueIds = await unitOfWork.RunAsync((services, token) =>
        {
            var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
            return dbContext.Subscriptions
                .AsNoTracking()
                .Where(x => x.Status == SubscriptionStatus.PastDue &&
                            x.GracePeriodEndsAt <= now &&
                            !dbContext.Payments.Any(p => p.SubscriptionId == x.Id &&
                                                         (p.Status == PaymentStatus.Submitted ||
                                                          p.Status == PaymentStatus.PartiallyPaid)))
                .OrderBy(x => x.GracePeriodEndsAt)
                .Select(x => x.Id)
                .ToListAsync(token);
        }, ct);

        var expired = 0;
        foreach (var id in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await unitOfWork.RunAsync((services, token) => ExpireAsync(services, id, now, token), ct))
                {
                    expired++;
                }
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() || ex is ConflictException)
            {
                logger.LogWarning(ex, "Expiry of subscription {SubscriptionId} skipped: {Message}", id, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Expiry of subscription {SubscriptionId} failed.", id);
            }
        }

        return expired;
    }

    private async Task<bool> ExpireAsync(IServiceProvider services, Guid subscriptionId, DateTimeOffset now, CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var repository = services.GetRequiredService<ISubscriptionRepository>();
        var cache = services.GetRequiredService<IDistributedCache>();

        var subscription = await repository.GetByIdAsync(subscriptionId, ct);
        if (subscription is null ||
            subscription.Status != SubscriptionStatus.PastDue ||
            subscription.GracePeriodEndsAt > now)
        {
            return false;
        }

        var awaiting = await repository.GetAwaitingPaymentsAsync(subscriptionId, ct);
        if (awaiting.Any(x => x.Status is PaymentStatus.Submitted or PaymentStatus.PartiallyPaid))
        {
            // A renewal proof is under review or money was partly received: the admin decides, the clock does not.
            return false;
        }

        subscription.Expire(now);
        foreach (var payment in awaiting.Where(x => x.Purpose == PaymentPurpose.Renewal))
        {
            payment.MarkFailed("Grace period elapsed");
        }

        LogTransition(dbContext, subscription, SubscriptionStatus.PastDue, "Grace period elapsed");
        await dbContext.SaveChangesAsync(ct);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), ct);

        logger.LogInformation("Expired subscription {SubscriptionId}.", subscription.Id);
        return true;
    }

    private void LogTransition(SubscriptionsDbContext dbContext, Subscription subscription, SubscriptionStatus from, string reason) =>
        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            from,
            subscription.Status,
            options.Value.SystemActorId,
            "System",
            reason));
}
