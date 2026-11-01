using Catalog.Contracts.Products.Queries;

namespace Catalog.Domains.Products.Features.GetProducts;

public class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
        RuleFor(x => x.Search).MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue);
        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue);
    }
}

/// <summary>
/// Query handlers read through the DbContext (not the repository) and project straight
/// to the DTO, so only the needed columns are selected and nothing is change-tracked.
/// </summary>
public class GetProductsHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetProductsQuery, PaginatedResult<ProductDto>>
{
    private const string LikeEscape = "\\";

    public async Task<PaginatedResult<ProductDto>> Handle(
        GetProductsQuery query,
        CancellationToken cancellationToken)
    {
        var products = dbContext.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{EscapeLike(query.Search.Trim())}%";
            products = products.Where(p =>
                EF.Functions.ILike(p.Name, pattern, LikeEscape) ||
                EF.Functions.ILike(p.Sku, pattern, LikeEscape));
        }

        if (query.IsActive.HasValue)
            products = products.Where(p => p.IsActive == query.IsActive.Value);
        if (query.MinPrice.HasValue)
            products = products.Where(p => p.Price >= query.MinPrice.Value);
        if (query.MaxPrice.HasValue)
            products = products.Where(p => p.Price <= query.MaxPrice.Value);

        var dtos = products
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ProjectToType<ProductDto>()
            // Unknown SortColumn values are ignored by OrderByDynamic (default order kept).
            .OrderByDynamic(query.SortColumn, query.SortBy);

        return await dtos.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }

    private static string EscapeLike(string value) =>
        value.Replace(LikeEscape, LikeEscape + LikeEscape)
            .Replace("%", LikeEscape + "%")
            .Replace("_", LikeEscape + "_");
}
