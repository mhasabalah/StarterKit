using ModuleName.Domains.Items.Exceptions;

namespace ModuleName.Domains.Items.Features.DeleteItem;

public class DeleteItemCommandValidator : AbstractValidator<DeleteItemCommand>
{
    public DeleteItemCommandValidator(IStringLocalizer<ModuleNameResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

/// <summary>Hard delete (deletes never take a version). Soft delete only when the domain really needs it.</summary>
public class DeleteItemHandler(IItemRepository repository) : ICommandHandler<DeleteItemCommand, DeleteItemResult>
{
    public async Task<DeleteItemResult> Handle(DeleteItemCommand command, CancellationToken cancellationToken)
    {
        var item = await repository.GetByIdAsync(command.Id, cancellationToken)
                   ?? throw new ItemNotFoundException(command.Id);

        repository.Remove(item);
        await repository.SaveChangesAsync(cancellationToken);
        return new DeleteItemResult(true);
    }
}
