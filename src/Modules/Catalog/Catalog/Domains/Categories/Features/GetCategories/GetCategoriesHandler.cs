namespace Catalog.Domains.Categories.Features.GetCategories;

/// <summary>
/// Admin flat list. Uses Mapster <c>ProjectToType</c> (see LookupMappingConfig) because the projection is
/// simple; the culture-specific DisplayName is filled in memory afterwards.
/// </summary>
public class GetCategoriesHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetCategoriesQuery, List<CategoryDto>>
{
    public async Task<List<CategoryDto>> Handle(GetCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories.AsNoTracking()
            .Where(c => query.IncludeInactive || c.IsActive)
            .OrderBy(c => c.ParentId)
            .ThenBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name.En)
            .ProjectToType<CategoryDto>()
            .ToListAsync(cancellationToken);

        return categories.Select(c => c with { DisplayName = c.Name.Resolve() }).ToList();
    }
}
