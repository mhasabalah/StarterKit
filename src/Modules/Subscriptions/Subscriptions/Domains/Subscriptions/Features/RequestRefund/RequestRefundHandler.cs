using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Features.RequestRefund;

public class RequestRefundCommandValidator : AbstractValidator<RequestRefundCommand>
{
    public RequestRefundCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
    }
}

public class RequestRefundHandler(
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext,
    IOptions<SubscriptionOptions> options)
    : ICommandHandler<RequestRefundCommand, Guid>
{
    public async Task<Guid> Handle(RequestRefundCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to request a refund for this subscription.");

        if (subscription.Status != SubscriptionStatus.Cancelled)
            throw new ConflictException("Refunds can only be requested for cancelled subscriptions.");

        if (subscription.BillingCycleInMonths != 12)
            throw new ConflictException("Monthly plans are not eligible for refunds.");

        var hasActiveRefund = await dbContext.RefundRequests
            .AsNoTracking()
            .AnyAsync(r => r.SubscriptionId == command.SubscriptionId
                        && (r.Status == RefundStatus.Requested
                         || r.Status == RefundStatus.UnderReview
                         || r.Status == RefundStatus.Approved),
                cancellationToken);

        if (hasActiveRefund)
            throw new ConflictException("A refund request is already pending for this subscription.");

        var now = DateTimeOffset.UtcNow;
        var startDate = subscription.StartDate
            ?? throw new ConflictException("Subscription has no activation date.");

        var proratedWindowEnd = startDate.AddDays(options.Value.RefundProratedWindowDays);
        if (now > proratedWindowEnd)
            throw new ConflictException("The refund window for this subscription has expired.");

        var fullWindowEnd = startDate.AddDays(options.Value.RefundFullWindowDays);
        RefundType refundType;
        decimal refundAmount;

        if (now <= fullWindowEnd)
        {
            refundType = RefundType.Full;
            refundAmount = subscription.Price;
        }
        else
        {
            refundType = RefundType.Prorated;
            var renewalDate = subscription.RenewalDate ?? startDate.AddMonths(subscription.BillingCycleInMonths);
            var remainingDays = (renewalDate - now).TotalDays;
            var totalDays = subscription.BillingCycleInMonths * 30m;
            refundAmount = Math.Round((decimal)(remainingDays / (double)totalDays) * subscription.Price, 2);
        }

        if (refundAmount <= 0)
            throw new ConflictException("Calculated refund amount is zero.");

        var refundRequest = SubscriptionRefundRequest.Create(
            subscription.Id,
            subscription.SubscriberId,
            subscription.PlanId,
            subscription.PlanName,
            subscription.BillingCycleInMonths,
            startDate,
            subscription.CancelledAt ?? now,
            subscription.RenewalDate ?? startDate.AddMonths(subscription.BillingCycleInMonths),
            refundAmount,
            subscription.Currency,
            refundType);

        await dbContext.RefundRequests.AddAsync(refundRequest, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return refundRequest.Id;
    }
}
