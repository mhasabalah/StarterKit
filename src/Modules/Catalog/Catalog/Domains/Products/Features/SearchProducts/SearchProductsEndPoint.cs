namespace Catalog.Domains.Products.Features.SearchProducts;

public class SearchProductsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products/search",
                async ([AsParameters] SearchProductsQuery query, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(query, cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("SearchProducts")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<PaginatedResult<ProductDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithSummary("Search Products")
            .WithDescription(
                "Search published products by `searchTerm` (name en/ar, SKU, description; '%' and '_' match " +
                "literally). Page 1 is public, page 2+ requires authentication. Rate limited.")
            .RequireAuthorization("PageGate")
            .RequireRateLimiting("search")
            .HasApiVersion(1.0);
    }
}
