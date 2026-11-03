using Catalog.Domains.Categories.Exceptions;

namespace Catalog.Domains.Categories.Features.CreateCategory;

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddCategoryFieldRules(localizer, x => x.Name, x => x.Slug, x => x.ParentId, x => x.DisplayOrder);
    }
}

public class CreateCategoryHandler(ICategoryRepository repository)
    : ICommandHandler<CreateCategoryCommand, CreateCategoryResult>
{
    public async Task<CreateCategoryResult> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        var slug = Category.NormalizeSlug(command.Slug);
        if (await repository.SlugExistsAsync(slug, cancellationToken: cancellationToken))
            throw new CategorySlugAlreadyExistsException(slug);

        if (command.ParentId is { } parentId && !await repository.ExistsAsync(parentId, cancellationToken))
            throw new InvalidCategoryException($"Parent category '{parentId}' does not exist.");

        var category = Category.Create(command.Name, command.Slug, command.ParentId, command.DisplayOrder);
        await repository.AddAsync(category, cancellationToken);

        return new CreateCategoryResult(category.Id);
    }
}
