using ModuleName.Domains.Items.Exceptions;

namespace ModuleName.Domains.Items.Features.GetItemById;

public class GetItemByIdQueryValidator : AbstractValidator<GetItemByIdQuery>
{
    public GetItemByIdQueryValidator(IStringLocalizer<ModuleNameResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

/// <summary>Query side: the DbContext, no tracking, projection in SQL.</summary>
public class GetItemByIdHandler(ModuleNameDbContext dbContext) : IQueryHandler<GetItemByIdQuery, ItemDto>
{
    public async Task<ItemDto> Handle(GetItemByIdQuery query, CancellationToken cancellationToken) =>
        await dbContext.Items.AsNoTracking()
            .Where(x => x.Id == query.Id)
            .Select(ItemRules.Projection)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new ItemNotFoundException(query.Id);
}
