using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.UpdatePlan;

public class UpdatePlanCommandValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanCommandValidator()
    {
        RuleFor(x => x.Name.En).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name.Ar).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.BillingCycleInMonths).Must(x => x is 1 or 12);
        When(x => x.Description is not null, () =>
        {
            RuleFor(x => x.Description!.En).MaximumLength(500);
            RuleFor(x => x.Description!.Ar).MaximumLength(500);
        });
    }
}

public class UpdatePlanHandler(
    IPlanRepository planRepository,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<UpdatePlanCommand, Unit>
{
    public async Task<Unit> Handle(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        // Set expected version for EF concurrency check
        dbContext.Entry(plan).Property(e => e.Version).OriginalValue = command.Version;

        plan.Update(
            command.Name,
            command.Description,
            command.Currency,
            command.BillingCycleInMonths,
            command.MaxItems,
            command.MaxStorageGb,
            command.MaxUsers,
            command.IsActive,
            command.IsPublic,
            command.DiscountPercent);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
