namespace Catalog.Domains.Products.Features.GetProducts;

public class GetProductsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products",
                async ([AsParameters] GetProductsQuery query, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(query with { Status = ProductStatus.Published }, cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetProducts")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<PaginatedResult<ProductDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithSummary("Get Products (Public)")
            .WithDescription(
                "Published products only (the status filter is ignored). Filters: search, type, categoryId " +
                "(+includeSubcategories), min/maxPrice, currency, tagIds (has all), attributeTypeIds (has all), " +
                "ownerId, isFeatured. Sort with `sortColumn`/`sortBy`. Page-gate: page 1 is public, page 2+ " +
                "requires authentication.")
            .RequireAuthorization("PageGate")
            .RequireRateLimiting("search")
            .HasApiVersion(1.0);
    }
}
