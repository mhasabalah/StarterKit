using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.GetPendingProducts;

public class GetPendingProductsQueryValidator : AbstractValidator<GetPendingProductsQuery>
{
    public GetPendingProductsQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddPagingRules(localizer);
        RuleFor(x => x.Search)
            .MaximumLength(CatalogValidationRules.SearchTermMaxLength)
            .WithMessage(_ => localizer["Validation.SearchTerm.MaxLength"]);
        RuleFor(x => x.Status).IsInEnum().WithMessage(_ => localizer["Validation.Status.Invalid"])
            .When(x => x.Status.HasValue);
    }
}

/// <summary>Admin moderation queue: PendingReview by default, oldest submission first (FIFO).</summary>
public class GetPendingProductsHandler(CatalogDbContext dbContext, ProductDtoMapper mapper)
    : IQueryHandler<GetPendingProductsQuery, PaginatedResult<PendingProductDto>>
{
    public async Task<PaginatedResult<PendingProductDto>> Handle(
        GetPendingProductsQuery query, CancellationToken cancellationToken)
    {
        var status = query.Status ?? ProductStatus.PendingReview;
        var products = dbContext.Products.AsNoTracking().Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(query.Search))
            products = ProductQueryBuilder.ApplySearch(products, query.Search);

        var page = await products
            .ToProductRows(dbContext.WishlistItems, Guid.Empty, includeGallery: false)
            .ApplySort(query, q => q.OrderBy(r => r.SubmittedForReviewAt), r => r.Id)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);

        var now = DateTime.UtcNow;
        return page.Map(row => mapper.ToPending(row, now));
    }
}
