using ModuleName.Domains.Items.Exceptions;

namespace ModuleName.Domains.Items.Features.UpdateItem;

public class UpdateItemCommandValidator : AbstractValidator<UpdateItemCommand>
{
    public UpdateItemCommandValidator(IStringLocalizer<ModuleNameResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        this.AddNameRules(x => x.Name, localizer);
        this.AddDescriptionRules(x => x.Description, localizer);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

/// <summary>
/// Optimistic concurrency: <c>ExpectVersion</c> makes SaveChanges fail when the row changed since the caller read
/// <see cref="UpdateItemCommand.Version"/>; ConcurrencyHandlingBehavior turns that into 409. Never touch Version by hand.
/// </summary>
public class UpdateItemHandler(IItemRepository repository, ModuleNameDbContext dbContext)
    : ICommandHandler<UpdateItemCommand, UpdateItemResult>
{
    public async Task<UpdateItemResult> Handle(UpdateItemCommand command, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(command.Id, cancellationToken)
                   ?? throw new ItemNotFoundException(command.Id);

        dbContext.ExpectVersion(item, command.Version);
        item.Update(command.Name, command.Description);

        await repository.SaveChangesAsync(cancellationToken);
        return new UpdateItemResult(item.Version);
    }
}
