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
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to modify this subscription.");

        subscription.CancelScheduledDowngrade(DateTimeOffset.UtcNow);

        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            subscription.Status,
            subscription.Status,
            command.SubscriberId,
            "Subscriber",
            "Cancelled scheduled downgrade"));

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
