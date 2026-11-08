namespace Catalog.Domains.Products.Features.GetPendingProducts;

public class GetPendingProductsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/products/pending",
                async ([AsParameters] GetPendingProductsQuery query, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(query, cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetPendingProducts")
            .WithTags("Product Workflow")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<PaginatedResult<PendingProductDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Get Moderation Queue")
            .WithDescription("Products waiting for review (default PendingReview), oldest first. Needs `catalog.moderate` (Admin, SuperAdmin).")
            .RequirePermission(Permissions.CatalogModerate)
            .HasApiVersion(1.0);
    }
}
