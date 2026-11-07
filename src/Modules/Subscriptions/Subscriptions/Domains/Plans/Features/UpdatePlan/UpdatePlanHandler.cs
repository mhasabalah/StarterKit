using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.UpdatePlan;

public class UpdatePlanCommandValidator : AbstractValidator<UpdatePlanCommand>
{
    public UpdatePlanCommandValidator()
    {
        RuleFor(x => x.Name.En).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name.Ar).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Currency).NotEmpty().Length(3).Must(CurrencyRules.IsKnown)
            .WithMessage("'{PropertyValue}' is not a valid ISO 4217 currency code.");
        RuleFor(x => x.TrialDays).InclusiveBetween(0, Plan.MaxTrialDays);
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
    SubscriptionsDbContext dbContext,
    IAdminAuditWriter audit)
    : ICommandHandler<UpdatePlanCommand, Unit>
{
    public async Task<Unit> Handle(UpdatePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        dbContext.ExpectVersion(plan, command.Version);
        var before = Snapshot(plan);

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
            command.DiscountPercent,
            command.TrialDays);

        audit.Record("UpdatePlan", plan.Id, "Plan", amount: plan.Price, currency: plan.Currency,
            before: before, after: Snapshot(plan));

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    private static object Snapshot(Plan plan) => new
    {
        Name = plan.Name.En,
        plan.Currency,
        plan.BillingCycleInMonths,
        plan.MaxItems,
        plan.MaxStorageGb,
        plan.MaxUsers,
        plan.IsActive,
        plan.IsPublic,
        plan.DiscountPercent
    };
}
