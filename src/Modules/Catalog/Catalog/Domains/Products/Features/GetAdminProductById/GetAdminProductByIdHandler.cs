using Shared.Core.MultiTenancy.Data;
using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.GetAdminProductById;

public class GetAdminProductByIdQueryValidator : AbstractValidator<GetAdminProductByIdQuery>
{
    public GetAdminProductByIdQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

/// <summary>Admin detail: any status, soft-deleted included, no view counting.</summary>
public class GetAdminProductByIdHandler(
    CatalogDbContext dbContext,
    IHttpContextService httpContextService,
    ProductDtoMapper mapper)
    : IQueryHandler<GetAdminProductByIdQuery, GetAdminProductByIdResult>
{
    public async Task<GetAdminProductByIdResult> Handle(
        GetAdminProductByIdQuery query, CancellationToken cancellationToken)
    {
        var row = await dbContext.Products.AsNoTracking().IgnoreNonIsolationFilters()
                      .Where(p => p.Id == query.Id)
                      .ToProductRows(dbContext.WishlistItems, httpContextService.GetUserIdAsGuid() ?? Guid.Empty,
                          includeGallery: true)
                      .AsSplitQuery()
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? throw new ProductNotFoundException(query.Id);

        return new GetAdminProductByIdResult(mapper.ToAdmin(row));
    }
}
