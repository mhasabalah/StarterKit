using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.CancelSubscription;

public class CancelSubscriptionCommandValidator : AbstractValidator<CancelSubscriptionCommand>
{
    public CancelSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
        RuleFor(x => x.CancellationNote).MaximumLength(1000);
    }
}

/// <summary>
/// Subscriber: Active → cancellation scheduled for the period end (applied by the renewal orchestrator);
/// Trialing or PastDue → cancelled now (nothing was paid for the period). Admin: cancelled now (audited).
/// </summary>
public class CancelSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IAdminAuditWriter audit,
    IDistributedCache cache,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<CancelSubscriptionCommand, Unit>
{
    public async Task<Unit> Handle(CancelSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (!command.IsAdminActor && subscription.SubscriberId != command.ActorId)
            throw new ForbiddenException("You are not allowed to cancel this subscription.");

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Trialing))
            throw new ConflictException("Only trialing, active or past due subscriptions can be cancelled.");

        var now = DateTimeOffset.UtcNow;
        var fromStatus = subscription.Status;
        var actorType = command.IsAdminActor ? "Admin" : "Subscriber";

        if (command.IsAdminActor || subscription.Status is SubscriptionStatus.PastDue or SubscriptionStatus.Trialing)
            subscription.CancelImmediately(command.ActorId, command.CancellationReason, command.CancellationNote, now);
        else
            subscription.ScheduleCancellation(command.ActorId, command.CancellationReason, command.CancellationNote, now);

        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            fromStatus,
            subscription.Status,
            command.ActorId,
            actorType,
            command.CancellationNote ?? command.Reason));

        if (command.IsAdminActor)
        {
            audit.Record(
                "CancelSubscription",
                subscription.Id,
                "Subscription",
                subscription.SubscriberId,
                command.CancellationNote ?? command.Reason,
                currency: subscription.Currency,
                before: new { Status = fromStatus.ToString() },
                after: new { Status = subscription.Status.ToString(), subscription.EndDate, Reason = command.CancellationReason?.ToString() },
                adminId: command.ActorId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);
        return Unit.Value;
    }
}
