using Catalog.Contracts.Products.Queries;

namespace Catalog.Domains.Products.Features.GetProducts;

public class GetProductsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products",
                async ([AsParameters] GetProductsQuery query, ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(query, cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetProducts")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<PaginatedResult<ProductDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .WithSummary("Get Products")
            .WithDescription(
                "Paginated product list. `search` matches name or SKU; sort with " +
                "`sortColumn` (any ProductDto property) and `sortBy`. Public.")
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
