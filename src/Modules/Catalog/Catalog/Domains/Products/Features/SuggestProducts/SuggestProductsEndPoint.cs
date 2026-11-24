namespace Catalog.Domains.Products.Features.SuggestProducts;

public class SuggestProductsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products/suggest",
                async ([AsParameters] SuggestProductsQuery query, ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, cancellationToken))))
            .WithName("SuggestProducts")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<List<ProductSuggestionDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithSummary("Suggest Products")
            .WithDescription(
                "As-you-type suggestions over published products: `q` matches the name (en/ar) and SKU, the last word as a " +
                "prefix (\"lap\" finds \"laptop\"), with close spellings when nothing matches. At most `limit` (1-20, default " +
                "8). Names in the request culture, with the typed text in `<mark>`. Empty while the search index is not " +
                "available. Public, rate limited.")
            .RequireAuthorization("PageGate")
            .RequireRateLimiting("search")
            .HasApiVersion(1.0);
    }
}
