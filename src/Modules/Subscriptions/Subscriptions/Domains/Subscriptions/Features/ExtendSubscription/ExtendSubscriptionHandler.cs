using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.ExtendSubscription;

public class ExtendSubscriptionCommandValidator : AbstractValidator<ExtendSubscriptionCommand>
{
    public ExtendSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Days).InclusiveBetween(1, Subscription.MaxExtensionDays);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Grants extra days (see <see cref="Subscription.Extend"/>); audited and written to the transition log.</summary>
public class ExtendSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext,
    IDistributedCache cache,
    TimeProvider timeProvider)
    : ICommandHandler<ExtendSubscriptionCommand, ExtendSubscriptionResult>
{
    public async Task<ExtendSubscriptionResult> Handle(ExtendSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.SubscriberId == command.AdminId)
            throw new SelfApprovalNotAllowedException();

        var before = new
        {
            Status = subscription.Status.ToString(),
            subscription.RenewalDate,
            subscription.GracePeriodEndsAt,
            subscription.ScheduledCancellationDate
        };

        subscription.Extend(command.Days, timeProvider.GetUtcNow());

        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            subscription.Status,
            subscription.Status,
            command.AdminId,
            "Admin",
            $"Extended by {command.Days} day(s): {command.Reason}"));

        audit.Record(
            "ExtendSubscription",
            subscription.Id,
            "Subscription",
            subscription.SubscriberId,
            command.Reason,
            before: before,
            after: new
            {
                Status = subscription.Status.ToString(),
                subscription.RenewalDate,
                subscription.GracePeriodEndsAt,
                subscription.ScheduledCancellationDate,
                command.Days
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);

        return new ExtendSubscriptionResult(
            subscription.Id, subscription.Status.ToString(), subscription.RenewalDate, subscription.GracePeriodEndsAt);
    }
}
