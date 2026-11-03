using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.GetMyProducts;

public class GetMyProductsQueryValidator : AbstractValidator<GetMyProductsQuery>
{
    public GetMyProductsQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddPagingRules(localizer);
        RuleFor(x => x.Search)
            .MaximumLength(CatalogValidationRules.SearchTermMaxLength)
            .WithMessage(_ => localizer["Validation.SearchTerm.MaxLength"]);
        RuleFor(x => x.Status).IsInEnum().WithMessage(_ => localizer["Validation.Status.Invalid"])
            .When(x => x.Status.HasValue);
    }
}

/// <summary>
/// The caller's own products (OwnerId = current user, any status) plus stats computed in ONE aggregate
/// query (<c>GroupBy(_ =&gt; 1)</c>) over all of them — the list filters do not affect the stats.
/// </summary>
public class GetMyProductsHandler(
    CatalogDbContext dbContext,
    IProductAuthorizationService authorization,
    ProductDtoMapper mapper)
    : IQueryHandler<GetMyProductsQuery, GetMyProductsResult>
{
    public async Task<GetMyProductsResult> Handle(GetMyProductsQuery query, CancellationToken cancellationToken)
    {
        var userId = authorization.RequireCurrentUserId();
        var mine = dbContext.Products.AsNoTracking().Where(p => p.OwnerId == userId);

        var stats = await mine
                        .GroupBy(_ => 1)
                        .Select(g => new MyProductStatsDto(
                            g.Count(),
                            g.Count(p => p.Status == ProductStatus.Draft),
                            g.Count(p => p.Status == ProductStatus.PendingReview || p.Status == ProductStatus.Approved),
                            g.Count(p => p.Status == ProductStatus.ChangesRequested || p.Status == ProductStatus.Rejected),
                            g.Count(p => p.Status == ProductStatus.Published),
                            g.Count(p => p.Status == ProductStatus.Archived),
                            g.Sum(p => p.ViewCount)))
                        .FirstOrDefaultAsync(cancellationToken)
                    ?? new MyProductStatsDto(0, 0, 0, 0, 0, 0, 0);

        var filtered = mine;
        if (query.Status.HasValue)
            filtered = filtered.Where(p => p.Status == query.Status.Value);
        if (query.Type.HasValue)
            filtered = filtered.Where(p => p.Type == query.Type.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
            filtered = ProductQueryBuilder.ApplySearch(filtered, query.Search);

        var page = await filtered
            .ToProductRows(dbContext.WishlistItems, userId, includeGallery: false)
            .Sorted(query)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);

        return new GetMyProductsResult(page.Map(mapper.ToMine), stats);
    }
}
