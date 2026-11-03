using Catalog.Domains.Categories.Exceptions;

namespace Catalog.Domains.Categories.Features.UpdateCategory;

public class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        this.AddCategoryFieldRules(localizer, x => x.Name, x => x.Slug, x => x.ParentId, x => x.DisplayOrder);
        RuleFor(x => x.ParentId)
            .NotEqual(x => x.Id).WithMessage(_ => localizer["Validation.ParentId.Self"])
            .When(x => x.ParentId.HasValue);
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

/// <summary>
/// Re-parenting is a real concurrency risk (two admins moving categories can build a loop), so the update
/// carries a version and checks the whole hierarchy for cycles before saving.
/// </summary>
public class UpdateCategoryHandler(ICategoryRepository repository, CatalogDbContext dbContext)
    : ICommandHandler<UpdateCategoryCommand, UpdateCategoryResult>
{
    public async Task<UpdateCategoryResult> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(command.Id, cancellationToken)
                       ?? throw new CategoryNotFoundException(command.Id);

        dbContext.ExpectVersion(category, command.Version);

        var slug = Category.NormalizeSlug(command.Slug);
        if (slug != category.Slug && await repository.SlugExistsAsync(slug, category.Id, cancellationToken))
            throw new CategorySlugAlreadyExistsException(slug);

        if (command.ParentId is { } parentId && parentId != category.ParentId)
        {
            var parents = await repository.GetParentMapAsync(cancellationToken);
            if (!parents.ContainsKey(parentId))
                throw new InvalidCategoryException($"Parent category '{parentId}' does not exist.");
            if (CategoryHierarchy.WouldCreateCycle(category.Id, parentId, parents))
                throw new InvalidCategoryException("A category cannot be moved under one of its own descendants.");
        }

        category.Update(command.Name, command.Slug, command.ParentId, command.DisplayOrder);
        if (command.IsActive) category.Activate();
        else category.Deactivate();

        await repository.SaveChangesAsync(cancellationToken);

        return new UpdateCategoryResult(true, category.Version);
    }
}
