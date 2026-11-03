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

/// <summary>
/// Requests a refund for a cancelled annual subscription. The amount is based on the MONEY RECEIVED for the current
/// period and on when service actually ended (decimal day math, actual period length), capped at
/// Σ paid − Σ approved/processed refunds. A second refund after one was processed is impossible (C11).
/// </summary>
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

        var existing = await dbContext.RefundRequests
            .AsNoTracking()
            .AnyAsync(r => r.SubscriptionId == command.SubscriptionId && r.Status != RefundStatus.Rejected, cancellationToken);
        if (existing)
            throw new ConflictException("A refund has already been requested or processed for this subscription.");

        var startDate = subscription.StartDate
            ?? throw new ConflictException("Subscription has no activation date.");
        var periodEnd = subscription.RenewalDate
            ?? throw new ConflictException("Subscription has no billing period.");
        var periodStart = subscription.EffectivePeriodStart ?? startDate;

        var input = await RefundInputs.BuildAsync(dbContext, subscription, periodStart, periodEnd, options.Value, cancellationToken);
        var quote = RefundPolicy.Quote(input);

        var refundRequest = SubscriptionRefundRequest.Create(
            subscription.Id,
            subscription.SubscriberId,
            subscription.PlanId,
            subscription.PlanName,
            subscription.BillingCycleInMonths,
            startDate,
            subscription.CancelledAt ?? input.ServiceEnd,
            periodEnd,
            quote.Amount,
            subscription.Currency,
            quote.Type);

        dbContext.RefundRequests.Add(refundRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return refundRequest.Id;
    }
}

/// <summary>Gathers the money facts for <see cref="RefundPolicy"/> from the database.</summary>
internal static class RefundInputs
{
    public static async Task<RefundInput> BuildAsync(
        SubscriptionsDbContext dbContext,
        Subscription subscription,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        SubscriptionOptions options,
        CancellationToken ct)
    {
        var paid = await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.SubscriptionId == subscription.Id && p.Status == PaymentStatus.Paid)
            .Select(p => new { p.Amount, p.Purpose, p.CreatedAt, p.PaidAt })
            .ToListAsync(ct);

        var refundable = await RefundableAsync(dbContext, subscription.Id, paid.Select(x => x.Amount), null, ct);

        var periodPayment = paid
            .Where(x => x.Purpose != PaymentPurpose.Proration)
            .OrderByDescending(x => x.PaidAt)
            .Select(x => x.Amount)
            .FirstOrDefault();
        var periodProrations = paid
            .Where(x => x.Purpose == PaymentPurpose.Proration && x.CreatedAt >= periodStart)
            .Sum(x => x.Amount);

        var serviceEnd = subscription.EndDate ?? subscription.CancelledAt ?? DateTimeOffset.UtcNow;

        return new RefundInput(
            periodPayment + periodProrations,
            refundable,
            periodStart,
            periodEnd,
            serviceEnd,
            options.RefundFullWindowDays,
            options.RefundProratedWindowDays);
    }

    /// <summary>Σ paid − Σ approved/processed refunds (optionally excluding one refund request).</summary>
    public static async Task<decimal> RefundableAsync(
        SubscriptionsDbContext dbContext,
        Guid subscriptionId,
        IEnumerable<decimal>? paidAmounts,
        Guid? excludeRefundId,
        CancellationToken ct)
    {
        var paid = paidAmounts?.ToList() ?? await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.SubscriptionId == subscriptionId && p.Status == PaymentStatus.Paid)
            .Select(p => p.Amount)
            .ToListAsync(ct);

        var refunded = await dbContext.RefundRequests
            .AsNoTracking()
            .Where(r => r.SubscriptionId == subscriptionId &&
                        (r.Status == RefundStatus.Approved || r.Status == RefundStatus.Processed) &&
                        (excludeRefundId == null || r.Id != excludeRefundId))
            .Select(r => r.RequestedAmount)
            .ToListAsync(ct);

        return RefundPolicy.Refundable(paid, refunded);
    }
}
