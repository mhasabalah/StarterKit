using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>Priced upgrade: proration for the rest of the current period, whether it is deferred, and the amount due now.</summary>
/// <param name="ProratedAmount">Prorated price difference (tax basis).</param>
/// <param name="AmountDue">Gross amount of the proration payment (0 when deferred).</param>
/// <param name="ProofRequiredBy">Deadline for paying a non-deferred proration (null when nothing is due now).</param>
public sealed record UpgradeQuote(
    decimal NewPlanPrice,
    decimal ProratedAmount,
    bool IsDeferred,
    decimal AmountDue,
    DateTimeOffset? ProofRequiredBy);

public interface IUpgradeTrustService
{
    Task<UpgradeQuote> QuoteAsync(Subscription subscription, Plan newPlan, DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// Prices an immediate upgrade. Proration covers only the CURRENT period (CurrentPeriodStart → RenewalDate), never
/// the whole lifetime. Trusted subscribers (≥ TrustMinCycles paid activation/renewal payments) may defer a small
/// proration (≤ ProratedDeferralThresholdPercent of the current price) to the next renewal.
/// </summary>
public class UpgradeTrustService(
    SubscriptionsDbContext dbContext,
    IOptions<SubscriptionOptions> options)
    : IUpgradeTrustService
{
    public async Task<UpgradeQuote> QuoteAsync(
        Subscription subscription,
        Plan newPlan,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var settings = options.Value;
        var newPrice = PricingPolicy.PlanPriceAt(newPlan, now);
        var periodEnd = subscription.RenewalDate ?? now;
        var periodStart = subscription.EffectivePeriodStart ?? now;

        var proration = PricingPolicy.UpgradeProration(subscription.Price, newPrice, periodStart, periodEnd, now);
        var trusted = await IsTrustedSubscriberAsync(subscription.SubscriberId, ct);
        var deferred = PricingPolicy.ShouldDeferProration(
            trusted, proration, subscription.Price, settings.ProratedDeferralThresholdPercent);

        var amountDue = deferred || proration <= 0m ? 0m : TaxCalculator.AmountDue(proration, settings.Tax.ToPolicy());
        DateTimeOffset? proofRequiredBy = amountDue > 0m ? now.AddHours(settings.UpgradeSlaHours) : null;

        return new UpgradeQuote(newPrice, proration, deferred, amountDue, proofRequiredBy);
    }

    private async Task<bool> IsTrustedSubscriberAsync(Guid subscriberId, CancellationToken ct)
    {
        var paidCycles = await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid &&
                        p.Purpose != PaymentPurpose.Proration &&
                        p.Subscription.SubscriberId == subscriberId)
            .CountAsync(ct);

        return paidCycles >= options.Value.TrustMinCycles;
    }
}
