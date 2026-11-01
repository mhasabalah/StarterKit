using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveSubscription;

public class ApproveSubscriptionCommandValidator : AbstractValidator<ApproveSubscriptionCommand>
{
    public ApproveSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

public class ApproveSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IInvoiceService invoiceService,
    IDistributedCache cache,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<ApproveSubscriptionCommand, ApproveSubscriptionResult>
{
    public async Task<ApproveSubscriptionResult> Handle(
        ApproveSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.Status != SubscriptionStatus.PendingApproval)
        {
            throw new ConflictException("Subscription is not pending approval.");
        }

        var latestPayment = subscription.Payments.OrderByDescending(x => x.CreatedAt).FirstOrDefault()
            ?? throw new PaymentNotFoundException(command.SubscriptionId);

        if (latestPayment.Status != PaymentStatus.Submitted)
        {
            throw new ConflictException("Latest payment is not submitted.");
        }
        if (latestPayment.Amount != subscription.Price ||
            !string.Equals(latestPayment.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentAmountMismatchException(
                subscription.Id,
                subscription.Price,
                subscription.Currency,
                latestPayment.Amount,
                latestPayment.Currency);
        }

        if (subscription.SubscriberId == command.AdminId)
        {
            throw new SelfApprovalNotAllowedException();
        }

        // Cancel any existing active/past-due subscription (plan-change scenario — preserve access until approval)
        var previousSub = await dbContext.Subscriptions
            .Where(x => x.SubscriberId == subscription.SubscriberId
                        && x.Id != subscription.Id
                        && (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue))
            .FirstOrDefaultAsync(cancellationToken);

        if (previousSub is not null)
        {
            var prevStatus = previousSub.Status;
            previousSub.CancelImmediately(command.AdminId, null, "Superseded by approved plan change");
            await dbContext.TransitionLogs.AddAsync(
                SubscriptionTransitionLog.Create(
                    previousSub.Id,
                    previousSub.SubscriberId,
                    prevStatus,
                    SubscriptionStatus.Cancelled,
                    command.AdminId,
                    "Admin",
                    "Superseded by approved plan change"),
                cancellationToken);
        }

        subscription.Activate();
        latestPayment.Approve(command.AdminId, subscription.SubscriberId);

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.PendingApproval,
                SubscriptionStatus.Active,
                command.AdminId,
                "Admin",
                null),
            cancellationToken);

        await dbContext.AdminAuditLogs.AddAsync(
            AdminActionAuditLog.Create(
                command.AdminId,
                "ApproveSubscription",
                subscription.Id,
                "Subscription",
                subscription.SubscriberId,
                null),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);

        await invoiceService.GenerateActivationInvoiceAsync(subscription, latestPayment.Id, cancellationToken);

        return new ApproveSubscriptionResult(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.StartDate!.Value,
            subscription.RenewalDate!.Value,
            Guid.NewGuid().ToString());
    }
}
