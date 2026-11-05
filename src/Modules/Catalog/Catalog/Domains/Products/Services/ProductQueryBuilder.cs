namespace Catalog.Domains.Products.Services;

/// <summary>
/// The standard filter pipeline for product list queries, shared by the public and admin handlers so
/// the filters cannot drift. Category descendants are resolved first (needs the DB), then everything
/// else is composed onto the IQueryable and translated to one SQL statement.
/// </summary>
public static class ProductQueryBuilder
{
    /// <summary>
    /// The category ids to match: null (no category filter), the single id, or the id plus all its
    /// descendants when <see cref="IProductFilterQuery.IncludeSubcategories"/> is true.
    /// </summary>
    public static async Task<IReadOnlyCollection<Guid>?> ResolveCategoryIdsAsync(
        CatalogDbContext dbContext, IProductFilterQuery query, CancellationToken cancellationToken)
    {
        if (query.CategoryId is not { } categoryId)
            return null;
        if (query.IncludeSubcategories != true)
            return [categoryId];

        // Inactive categories included on purpose: their products are still filed under them.
        var all = await dbContext.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync(cancellationToken);

        return CategoryHierarchy.GetDescendantIds(categoryId, all.Select(c => (c.Id, c.ParentId)));
    }

    public static IQueryable<Product> ApplyFilters(
        IQueryable<Product> products, IProductFilterQuery query, IReadOnlyCollection<Guid>? categoryIds)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
            products = ApplySearch(products, query.Search);

        if (query.Type.HasValue)
            products = products.Where(p => p.Type == query.Type.Value);

        if (query.Status.HasValue)
            products = products.Where(p => p.Status == query.Status.Value);

        if (categoryIds is not null)
            products = products.Where(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value));

        if (query.MinPrice.HasValue)
            products = products.Where(p => p.Pricing.Price >= query.MinPrice.Value);

        if (query.MaxPrice.HasValue)
            products = products.Where(p => p.Pricing.Price <= query.MaxPrice.Value);

        if (query.Currency.HasValue)
            products = products.Where(p => p.Pricing.Currency == query.Currency.Value);

        // "Has ALL" semantics: one EXISTS per requested tag / attribute type.
        foreach (var tagId in query.TagIds?.Distinct() ?? [])
            products = products.Where(p => p.Tags.Any(t => t.TagId == tagId));

        foreach (var attributeTypeId in query.AttributeTypeIds?.Distinct() ?? [])
            products = products.Where(p => p.Attributes.Any(a => a.AttributeTypeId == attributeTypeId));

        if (query.OwnerId.HasValue)
            products = products.Where(p => p.OwnerId == query.OwnerId.Value);

        if (query.IsFeatured.HasValue)
            products = products.Where(p => p.IsFeatured == query.IsFeatured.Value);

        return products;
    }

    /// <summary>Case-insensitive contains on name (en/ar), SKU and description, with LIKE wildcards escaped.</summary>
    public static IQueryable<Product> ApplySearch(IQueryable<Product> products, string term)
    {
        var pattern = LikePattern.Contains(term);
        return products.Where(p =>
            EF.Functions.ILike(p.Name.En, pattern, LikePattern.EscapeCharacter) ||
            EF.Functions.ILike(p.Name.Ar, pattern, LikePattern.EscapeCharacter) ||
            EF.Functions.ILike(p.Sku, pattern, LikePattern.EscapeCharacter) ||
            (p.Description != null && EF.Functions.ILike(p.Description, pattern, LikePattern.EscapeCharacter)));
    }
}
