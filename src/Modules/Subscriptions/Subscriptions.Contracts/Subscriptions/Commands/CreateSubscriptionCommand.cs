namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="CouponCode">Optional coupon redeemed together with the subscription (discounts the activation bill).</param>
public record CreateSubscriptionCommand(Guid SubscriberId, Guid PlanId, string? CouponCode = null)
    : ICommand<CreateSubscriptionResult>;
