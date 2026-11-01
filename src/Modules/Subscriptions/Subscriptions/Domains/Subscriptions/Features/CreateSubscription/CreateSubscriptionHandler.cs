using System.Text.Json;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Plans.Exceptions;

namespace Subscriptions.Domains.Subscriptions.Features.CreateSubscription;

public class CreateSubscriptionCommandValidator : AbstractValidator<CreateSubscriptionCommand>
{
    public CreateSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
    }
}

public class CreateSubscriptionHandler(
    IPlanRepository planRepository,
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<CreateSubscriptionCommand, CreateSubscriptionResult>
{
    public async Task<CreateSubscriptionResult> Handle(
        CreateSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var hasActive = await subscriptionRepository.HasActiveSubscriptionAsync(
            command.SubscriberId,
            cancellationToken);
        if (hasActive)
        {
            throw new ActiveSubscriptionAlreadyExistsException();
        }

        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken);
        if (plan is null || !plan.IsActive || !plan.IsPublic || plan.IsDeleted)
        {
            throw new PlanNotFoundException(command.PlanId);
        }

        var featuresJson = JsonSerializer.Serialize(
            plan.Features.ToDictionary(x => x.Key, _ => "true"));
        var idempotencyToken = Guid.NewGuid().ToString();
        var subscription = Subscription.Create(command.SubscriberId, plan, featuresJson, idempotencyToken);
        var payment = SubscriptionPayment.Create(subscription.Id, plan.Price, plan.Currency);
        subscription.AddPayment(payment);

        await subscriptionRepository.AddAsync(subscription, cancellationToken);
        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                command.SubscriberId,
                null,
                SubscriptionStatus.PendingApproval,
                command.SubscriberId,
                "Subscriber",
                null),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateSubscriptionResult(
            subscription.Id,
            idempotencyToken,
            subscription.Status.ToString(),
            subscription.PlanName,
            subscription.Price,
            subscription.Currency,
            subscription.BillingCycleInMonths);
    }
}
