using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.CancelScheduledDowngrade;

public class CancelScheduledDowngradeCommandValidator : AbstractValidator<CancelScheduledDowngradeCommand>
{
    public CancelScheduledDowngradeCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
    }
}

public class CancelScheduledDowngradeHandler(
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<CancelScheduledDowngradeCommand, Unit>
{
    public async Task<Unit> Handle(
        CancelScheduledDowngradeCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdForUpdateAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to modify this subscription.");

        subscription.CancelScheduledDowngrade(command.SubscriberId);

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.Active,
                SubscriptionStatus.Active,
                command.SubscriberId,
                "Subscriber",
                "Cancelled scheduled downgrade"),
            cancellationToken);

        await subscriptionRepository.UpdateAsync(subscription, cancellationToken);
        return Unit.Value;
    }
}
