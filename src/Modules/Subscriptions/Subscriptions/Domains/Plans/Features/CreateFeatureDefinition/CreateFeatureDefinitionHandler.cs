using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Plans.Models;

namespace Subscriptions.Domains.Plans.Features.CreateFeatureDefinition;

public class CreateFeatureDefinitionCommandValidator : AbstractValidator<CreateFeatureDefinitionCommand>
{
    public CreateFeatureDefinitionCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100)
            .Matches(@"^[a-z0-9]+(\.[a-z0-9]+)*$")
            .WithMessage("Key must be lowercase dot-separated segments, e.g. 'analytics.advanced'.");
        RuleFor(x => x.DisplayName).NotNull();
        RuleFor(x => x.DisplayName.En).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DisplayName.Ar).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class CreateFeatureDefinitionHandler(SubscriptionsDbContext dbContext)
    : ICommandHandler<CreateFeatureDefinitionCommand, string>
{
    public async Task<string> Handle(CreateFeatureDefinitionCommand command, CancellationToken cancellationToken)
    {
        var exists = await dbContext.FeatureDefinitions
            .AnyAsync(x => x.Key == command.Key, cancellationToken);

        if (exists)
            throw new ConflictException($"Feature definition '{command.Key}' already exists.");

        var definition = PlanFeatureDefinition.Create(
            command.Key,
            command.DisplayName,
            command.Description,
            command.Category,
            command.SortOrder);

        await dbContext.FeatureDefinitions.AddAsync(definition, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return definition.Key;
    }
}
