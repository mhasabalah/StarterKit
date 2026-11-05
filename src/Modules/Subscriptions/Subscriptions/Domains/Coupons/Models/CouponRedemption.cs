namespace Subscriptions.Domains.Coupons.Models;

/// <summary>
/// One subscriber redeeming one coupon. Unique per (coupon, subscriber): the unique index is the guarantee that a
/// subscriber cannot reuse a code even when two requests race.
/// </summary>
public class CouponRedemption
{
    public Guid Id { get; private set; }
    public Guid CouponId { get; private set; }
    public Guid SubscriberId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public DateTimeOffset RedeemedAt { get; private set; }

    private CouponRedemption()
    {
    }

    public static CouponRedemption Create(Guid couponId, Guid subscriberId, Guid subscriptionId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            CouponId = couponId,
            SubscriberId = subscriberId,
            SubscriptionId = subscriptionId,
            RedeemedAt = now
        };
}
