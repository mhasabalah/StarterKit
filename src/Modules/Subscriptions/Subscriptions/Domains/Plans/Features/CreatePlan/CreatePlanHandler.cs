using Subscriptions.Domains.Subscriptions.Services;
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
        RuleFor(x => x.TrialDays).InclusiveBetween(0, Plan.MaxTrialDays);
        RuleFor(x => x.BillingCycleInMonths).Must(x => x is 1 or 12);
    }
}

public class CreatePlanHandler(IPlanRepository planRepository, IAdminAuditWriter audit)
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
            command.DiscountPercent,
            trialDays: command.TrialDays);

        audit.Record("CreatePlan", plan.Id, "Plan", amount: plan.Price, currency: plan.Currency,
            after: new { Name = plan.Name.En, plan.Price, plan.Currency, plan.BillingCycleInMonths, plan.DiscountPercent, plan.IsPublic });
        await planRepository.AddAsync(plan, cancellationToken);
        return plan.Id;
    }
}
