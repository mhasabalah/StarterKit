using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.CancelSubscription;

public class CancelSubscriptionCommandValidator : AbstractValidator<CancelSubscriptionCommand>
{
    public CancelSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
    }
}

public class CancelSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IDistributedCache cache,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<CancelSubscriptionCommand, Unit>
{
    public async Task<Unit> Handle(CancelSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdForUpdateAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue))
        {
            throw new ConflictException("Only active or past due subscriptions can be cancelled.");
        }

        if (!command.IsAdminActor && subscription.SubscriberId != command.ActorId)
        {
            throw new ForbiddenException("You are not allowed to cancel this subscription.");
        }

        var fromStatus = subscription.Status;
        SubscriptionStatus toStatus;
        string actorType = command.IsAdminActor ? "Admin" : "Subscriber";

        if (command.IsAdminActor || subscription.Status == SubscriptionStatus.PastDue)
        {
            subscription.CancelImmediately(command.ActorId, command.CancellationReason, command.CancellationNote);
            toStatus = SubscriptionStatus.Cancelled;
        }
        else
        {
            subscription.ScheduleCancellation(command.ActorId, command.CancellationReason, command.CancellationNote);
            toStatus = SubscriptionStatus.Active;
        }

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                fromStatus,
                toStatus,
                command.ActorId,
                actorType,
                command.CancellationNote ?? command.Reason),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);
        return Unit.Value;
    }
}
