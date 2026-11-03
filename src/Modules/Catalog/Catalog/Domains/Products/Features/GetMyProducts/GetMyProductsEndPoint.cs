namespace Catalog.Domains.Products.Features.GetMyProducts;

public class GetMyProductsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products/my",
                async ([AsParameters] GetMyProductsQuery query, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(query, cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetMyProducts")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<GetMyProductsResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Get My Products")
            .WithDescription(
                "The caller's own products in every status (with status label and reviewer notes) plus stats. " +
                "Filters: status, type, search. Requires authentication.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
