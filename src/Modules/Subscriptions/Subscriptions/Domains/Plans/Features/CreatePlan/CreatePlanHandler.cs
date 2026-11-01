using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.CreatePlan;

public class CreatePlanCommandValidator : AbstractValidator<CreatePlanCommand>
{
    public CreatePlanCommandValidator()
    {
        RuleFor(x => x.Name).NotNull();
        RuleFor(x => x.Name.En).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name.Ar).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.BillingCycleInMonths).Must(x => x is 1 or 12);
    }
}

public class CreatePlanHandler(IPlanRepository planRepository)
    : ICommandHandler<CreatePlanCommand, Guid>
{
    public async Task<Guid> Handle(CreatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = Plan.Create(
            command.Name,
            command.Description,
            command.Price,
            command.Currency,
            command.BillingCycleInMonths,
            command.MaxItems,
            command.MaxStorageGb,
            command.MaxUsers,
            command.IsPublic,
            command.DiscountPercent);

        await planRepository.AddAsync(plan, cancellationToken);
        return plan.Id;
    }
}
