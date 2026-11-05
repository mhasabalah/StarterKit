namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Redeems a coupon on the subscriber's current subscription (Trialing, Active or PastDue); the discount applies to the
/// next bills. A coupon for a new subscription is passed on <see cref="CreateSubscriptionCommand"/> instead.
/// </summary>
public record RedeemCouponCommand(Guid SubscriberId, string Code) : ICommand<RedeemCouponResult>;

/// <param name="PeriodsRemaining">Billing periods the coupon will still discount; null = forever.</param>
public record RedeemCouponResult(
    Guid CouponId,
    string Code,
    string DiscountType,
    decimal Value,
    int? PeriodsRemaining);
