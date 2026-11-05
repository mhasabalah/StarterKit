using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Coupons.Services;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Features.CreateSubscription;

public class CreateSubscriptionCommandValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.CouponCode).MaximumLength(Coupon.MaxCodeLength);
    }
}

/// <summary>
/// Creates a PendingApproval subscription + its activation payment. The pre-check gives a friendly 409; the filtered
/// unique index (one open subscription per subscriber) makes concurrent creates safe (the loser gets a 409 too).
/// </summary>
public class CreateSubscriptionHandler(
    IPlanRepository planRepository,
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext,
    ISubscriberInfoProvider subscriberInfoProvider,
    IOptions<SubscriptionOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<CreateSubscriptionCommand, CreateSubscriptionResult>
{
    public async Task<CreateSubscriptionResult> Handle(
        CreateSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        if (await subscriptionRepository.HasOpenSubscriptionAsync(command.SubscriberId, cancellationToken))
        {
            throw new ActiveSubscriptionAlreadyExistsException();
        }

        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken);
        if (plan is null || !plan.IsActive || !plan.IsPublic || plan.IsDeleted)
        {
            throw new PlanNotFoundException(command.PlanId);
        }

        var now = timeProvider.GetUtcNow();
        var price = PricingPolicy.PlanPriceAt(plan, now);

        // A coupon discounts the activation bill (after the plan's own discount) and the following bills per its duration.
        var coupon = string.IsNullOrWhiteSpace(command.CouponCode)
            ? null
            : await CouponLookup.FindRedeemableAsync(
                dbContext, command.CouponCode, command.SubscriberId, plan.Id, plan.Currency, now, cancellationToken);

        var (amountDue, couponDiscount) = PricingPolicy.ActivationCharge(price, options.Value.Tax.ToPolicy(), coupon?.Terms);

        var subscription = Subscription.Create(
            command.SubscriberId, PlanSnapshot.From(plan, price), amountDue, now, coupon?.Terms, couponDiscount);
        var payment = subscription.ActivationPayment;
        if (coupon is not null)
            dbContext.CouponRedemptions.Add(coupon.Redeem(command.SubscriberId, subscription.Id, plan.Id, plan.Currency, now));
        await PaymentSettlementService.RefreshSubscriberSnapshotAsync(subscriberInfoProvider, subscription, cancellationToken);

        await subscriptionRepository.AddAsync(subscription, cancellationToken);
        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            command.SubscriberId,
            null,
            SubscriptionStatus.PendingApproval,
            command.SubscriberId,
            "Subscriber",
            null));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateSubscriptionResult(
            subscription.Id,
            payment.Id,
            subscription.Status.ToString(),
            subscription.PlanName,
            subscription.Price,
            payment.Amount,
            subscription.Currency,
            subscription.BillingCycleInMonths);
    }
}
