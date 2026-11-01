using Catalog.Contracts.Products.Dtos;

namespace Catalog.Contracts.Products.Queries;

public record GetProductByIdQuery(Guid Id) : IQuery<GetProductByIdResult>;

public record GetProductByIdResult(ProductDto Product);
