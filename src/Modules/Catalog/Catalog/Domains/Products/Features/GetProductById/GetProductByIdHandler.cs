using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.GetProductById;

public class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

/// <summary>
/// Public detail with the full gallery. Visible when Published, or to its owner in any status.
/// View counter: a read of a published product by anyone but its owner increments it with one atomic
/// <c>UPDATE ... SET view_count = view_count + 1</c> (ExecuteUpdate) — no load/modify/save race and no
/// Version bump, so counting views never causes 409s for editors.
/// </summary>
public class GetProductByIdHandler(
    CatalogDbContext dbContext,
    IHttpContextService httpContextService,
    ProductDtoMapper mapper)
    : IQueryHandler<GetProductByIdQuery, GetProductByIdResult>
{
    public async Task<GetProductByIdResult> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid() ?? Guid.Empty;

        var row = await dbContext.Products.AsNoTracking()
                      .Where(p => p.Id == query.Id && (p.Status == ProductStatus.Published || p.OwnerId == userId))
                      .ToProductRows(dbContext.WishlistItems, userId, includeGallery: true)
                      .AsSplitQuery()
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? throw new ProductNotFoundException(query.Id);

        if (query.IncrementView && row.Status == ProductStatus.Published && row.OwnerId != userId)
        {
            await dbContext.Products
                .Where(p => p.Id == query.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), cancellationToken);

            row = row with { ViewCount = row.ViewCount + 1 };
        }

        return new GetProductByIdResult(mapper.ToPublic(row));
    }
}
