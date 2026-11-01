using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.UpdateFeatureDefinition;

public class UpdateFeatureDefinitionCommandValidator : AbstractValidator<UpdateFeatureDefinitionCommand>
{
    public UpdateFeatureDefinitionCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotNull();
        RuleFor(x => x.DisplayName.En).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DisplayName.Ar).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class UpdateFeatureDefinitionHandler(SubscriptionsDbContext dbContext)
    : ICommandHandler<UpdateFeatureDefinitionCommand>
{
    public async Task<Unit> Handle(UpdateFeatureDefinitionCommand command, CancellationToken cancellationToken)
    {
        var definition = await dbContext.FeatureDefinitions
            .FirstOrDefaultAsync(x => x.Key == command.Key, cancellationToken)
            ?? throw new NotFoundException($"Feature definition '{command.Key}' was not found.");

        definition.Update(command.DisplayName, command.Description, command.Category, command.SortOrder);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
