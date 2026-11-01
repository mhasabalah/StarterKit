using Catalog.Contracts.Products.Queries;

namespace Catalog.Domains.Products.Features.GetProductById;

public class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class GetProductByIdHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetProductByIdQuery, GetProductByIdResult>
{
    public async Task<GetProductByIdResult> Handle(
        GetProductByIdQuery query,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
                          .AsNoTracking()
                          .Where(p => p.Id == query.Id)
                          .ProjectToType<ProductDto>()
                          .FirstOrDefaultAsync(cancellationToken)
                      ?? throw new ProductNotFoundException(query.Id);

        return new GetProductByIdResult(product);
    }
}
