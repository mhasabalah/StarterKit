using Subscriptions.Domains.Coupons.Exceptions;

namespace Subscriptions.Domains.Coupons.Services;

/// <summary>Finds a coupon by code and checks that THIS subscriber can use it on THIS plan right now.</summary>
public static class CouponLookup
{
    public static async Task<Coupon> FindRedeemableAsync(
        SubscriptionsDbContext dbContext,
        string code,
        Guid subscriberId,
        Guid planId,
        string currency,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalized = Coupon.NormalizeCode(code);
        var coupon = await dbContext.Coupons.FirstOrDefaultAsync(x => x.Code == normalized, ct)
            ?? throw new CouponNotFoundException(normalized);

        coupon.EnsureRedeemable(planId, currency, now);

        // Friendly 409; the unique (coupon, subscriber) index is the guarantee under a race.
        if (await dbContext.CouponRedemptions.AnyAsync(x => x.CouponId == coupon.Id && x.SubscriberId == subscriberId, ct))
            throw new CouponAlreadyRedeemedException();

        return coupon;
    }
}
