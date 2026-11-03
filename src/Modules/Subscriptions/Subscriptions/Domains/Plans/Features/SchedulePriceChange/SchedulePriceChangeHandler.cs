using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.SchedulePriceChange;

public class SchedulePriceChangeCommandValidator : AbstractValidator<SchedulePriceChangeCommand>
{
    public SchedulePriceChangeCommandValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.NewPrice).GreaterThan(0);
        RuleFor(x => x.EffectiveAt).GreaterThan(DateTimeOffset.UtcNow.AddDays(7))
            .WithMessage("Effective date must be at least 7 days in the future.");
    }
}

public class SchedulePriceChangeHandler(
    IPlanRepository planRepository,
    SubscriptionsDbContext dbContext,
    IAdminAuditWriter audit)
    : ICommandHandler<SchedulePriceChangeCommand, Unit>
{
    public async Task<Unit> Handle(SchedulePriceChangeCommand command, CancellationToken cancellationToken)
    {
        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        if (!plan.IsActive || plan.IsDeleted)
            throw new PlanNotFoundException(command.PlanId);

        if (plan.PendingPrice is not null)
            throw new ConflictException(
                $"A price change to {plan.Currency} {plan.PendingPrice.Value:F2} is already scheduled for {plan.PriceEffectiveAt:yyyy-MM-dd}. Cancel it before scheduling a new one.");

        if (command.NewPrice == plan.Price)
            throw new ConflictException("New price is the same as the current price.");

        plan.SchedulePriceChange(command.NewPrice, command.EffectiveAt);

        audit.Record(
            "SchedulePriceChange",
            plan.Id,
            "Plan",
            reason: $"Price change from {plan.Currency} {plan.Price:F2} to {plan.Currency} {command.NewPrice:F2}, effective {command.EffectiveAt:yyyy-MM-dd}",
            amount: command.NewPrice, currency: plan.Currency, before: new { Price = plan.Price }, after: new { PendingPrice = command.NewPrice, command.EffectiveAt },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
