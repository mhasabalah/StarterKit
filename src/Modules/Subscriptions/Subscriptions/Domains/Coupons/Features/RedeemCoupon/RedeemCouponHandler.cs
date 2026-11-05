using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Coupons.Services;

namespace Subscriptions.Domains.Coupons.Features.RedeemCoupon;

public class RedeemCouponCommandValidator : AbstractValidator<RedeemCouponCommand>
{
    public RedeemCouponCommandValidator()
    {
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Coupon.MaxCodeLength);
    }
}

/// <summary>
/// Redeems a coupon on the subscriber's current subscription: the coupon terms are snapshotted on it and discount the
/// NEXT bills (the renewal, or the first paid bill after a trial). The coupon aggregate's version makes the last
/// remaining redemption safe under concurrency (the loser gets a 409).
/// </summary>
public class RedeemCouponHandler(
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<RedeemCouponCommand, RedeemCouponResult>
{
    public async Task<RedeemCouponResult> Handle(RedeemCouponCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetOpenBySubscriberIdAsync(command.SubscriberId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriberId);

        if (subscription.Status == SubscriptionStatus.PendingApproval)
            throw new ConflictException(
                "The first bill of a new subscription is already issued; pass the coupon code when creating the subscription.");

        var now = timeProvider.GetUtcNow();
        var coupon = await CouponLookup.FindRedeemableAsync(
            dbContext, command.Code, command.SubscriberId, subscription.PlanId, subscription.Currency, now, cancellationToken);

        var redemption = coupon.Redeem(command.SubscriberId, subscription.Id, subscription.PlanId, subscription.Currency, now);
        subscription.RedeemCoupon(coupon.Terms, now);
        dbContext.CouponRedemptions.Add(redemption);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RedeemCouponResult(
            coupon.Id,
            coupon.Code,
            coupon.DiscountType.ToString(),
            coupon.Value,
            subscription.CouponPeriodsRemaining);
    }
}
