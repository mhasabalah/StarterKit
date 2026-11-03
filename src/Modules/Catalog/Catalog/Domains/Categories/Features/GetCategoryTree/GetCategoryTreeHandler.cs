using Catalog.Domains.Categories.Cache;

namespace Catalog.Domains.Categories.Features.GetCategoryTree;

/// <summary>
/// Public tree: snapshot from Redis (or the DB on a miss, then cached), localized in memory.
/// </summary>
public class GetCategoryTreeHandler(CatalogDbContext dbContext, ICategoryTreeCache cache)
    : IQueryHandler<GetCategoryTreeQuery, List<CategoryTreeNodeDto>>
{
    public async Task<List<CategoryTreeNodeDto>> Handle(GetCategoryTreeQuery query, CancellationToken cancellationToken)
    {
        var nodes = await cache.GetAsync(cancellationToken);
        if (nodes is null)
        {
            nodes = await dbContext.Categories.AsNoTracking()
                .Where(c => c.IsActive)
                .Select(c => new CategoryNodeSnapshot(c.Id, c.ParentId, c.Name.En, c.Name.Ar, c.Slug, c.DisplayOrder))
                .ToListAsync(cancellationToken);

            await cache.SetAsync(nodes, cancellationToken);
        }

        return CategoryHierarchy.BuildTree(nodes.ToList(), CatalogCulture.IsArabic());
    }
}
