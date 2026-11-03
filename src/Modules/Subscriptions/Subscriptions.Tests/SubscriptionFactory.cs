using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.Tests;

/// <summary>Builds subscriptions in a given state by driving only valid domain transitions.</summary>
internal static class SubscriptionFactory
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 1, 31, 9, 0, 0, TimeSpan.Zero);
    public static readonly TaxPolicy Tax = new(0.15m, PricesIncludeTax: true);

    public static PlanSnapshot Plan(decimal price, string currency = "SAR", int cycle = 1, string name = "Plan") =>
        new(Guid.NewGuid(), name, price, currency, cycle, 10, 5, 3, "{}");

    public static Subscription Pending(decimal price = 100m, string currency = "SAR", int cycle = 1, DateTimeOffset? now = null) =>
        Subscription.Create(Guid.NewGuid(), Plan(price, currency, cycle), price, now ?? DefaultNow);

    public static Subscription Active(decimal price = 100m, string currency = "SAR", int cycle = 1, DateTimeOffset? now = null)
    {
        var subscription = Pending(price, currency, cycle, now);
        subscription.Activate(now ?? DefaultNow);
        return subscription;
    }

    public static Subscription InStatus(SubscriptionStatus status)
    {
        var now = DefaultNow;
        switch (status)
        {
            case SubscriptionStatus.PendingApproval:
                return Pending();
            case SubscriptionStatus.Rejected:
            {
                var s = Pending();
                s.Reject("no", now);
                return s;
            }
            case SubscriptionStatus.Active:
                return Active();
            case SubscriptionStatus.PastDue:
            {
                var s = Active();
                s.MarkPastDue(PricingPolicy.RenewalCharge(s.Price, 0m, Tax), 7, s.RenewalDate!.Value);
                return s;
            }
            case SubscriptionStatus.Expired:
            {
                var s = InStatus(SubscriptionStatus.PastDue);
                s.Expire(s.GracePeriodEndsAt!.Value);
                return s;
            }
            case SubscriptionStatus.Cancelled:
            {
                var s = Active();
                s.CancelImmediately(Guid.NewGuid(), null, null, now.AddDays(1));
                return s;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}
