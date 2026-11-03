using Catalog.Domains.Categories.Exceptions;

namespace Catalog.Domains.Categories.Features.DeleteCategory;

public class DeleteCategoryCommandValidator : AbstractValidator<DeleteCategoryCommand>
{
    public DeleteCategoryCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
    }
}

/// <summary>"Delete" = deactivate: products keep their category; the public tree hides it (and its subtree).</summary>
public class DeleteCategoryHandler(ICategoryRepository repository)
    : ICommandHandler<DeleteCategoryCommand, DeleteCategoryResult>
{
    public async Task<DeleteCategoryResult> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(command.Id, cancellationToken)
                       ?? throw new CategoryNotFoundException(command.Id);

        category.Deactivate();
        await repository.SaveChangesAsync(cancellationToken);

        return new DeleteCategoryResult(true);
    }
}
