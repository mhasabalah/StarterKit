using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

public class UpgradeTrustService(
    SubscriptionsDbContext dbContext,
    IOptions<SubscriptionOptions> options)
    : IUpgradeTrustService
{
    public async Task<UpgradeTrustEvaluation> EvaluateAsync(
        Subscription subscription,
        Plan newPlan,
        CancellationToken ct = default)
    {
        var proratedAmount = CalculateProration(subscription, newPlan);
        var isTrusted = await IsTrustedSubscriberAsync(subscription.SubscriberId, ct);

        var thresholdPercent = options.Value.ProratedDeferralThresholdPercent;
        var threshold = subscription.Price * thresholdPercent / 100m;

        var isDeferred = isTrusted && proratedAmount <= threshold;
        var proofRequiredBy = isDeferred
            ? null
            : (DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(options.Value.UpgradeSlaHours);

        return new UpgradeTrustEvaluation(isDeferred, proratedAmount, proofRequiredBy);
    }

    private static decimal CalculateProration(Subscription subscription, Plan newPlan)
    {
        var priceDiff = newPlan.Price - subscription.Price;
        if (priceDiff <= 0)
            return 0;

        if (subscription.RenewalDate is null || subscription.StartDate is null)
            return priceDiff;

        var totalCycleDays = (subscription.RenewalDate.Value - subscription.StartDate.Value).TotalDays;
        if (totalCycleDays <= 0)
            return priceDiff;

        var remainingDays = (subscription.RenewalDate.Value - DateTimeOffset.UtcNow).TotalDays;
        if (remainingDays <= 0)
            return 0;

        var ratio = remainingDays / totalCycleDays;
        return Math.Round((decimal)ratio * priceDiff, 2);
    }

    private async Task<bool> IsTrustedSubscriberAsync(Guid subscriberId, CancellationToken ct)
    {
        var minCycles = options.Value.TrustMinCycles;

        var paidCycles = await dbContext.Payments
            .AsNoTracking()
            .Join(dbContext.Subscriptions,
                p => p.SubscriptionId,
                s => s.Id,
                (p, s) => new { p, s })
            .Where(x => x.s.SubscriberId == subscriberId && x.p.Status == PaymentStatus.Paid)
            .CountAsync(ct);

        return paidCycles >= minCycles;
    }
}
