namespace Catalog.Domains.Products.Features.RejectProduct;

public record RejectProductRequest(string Reason, long? Version = null);

public class RejectProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/reject",
                async ([FromRoute] Guid id, [FromBody] RejectProductRequest request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new RejectProductCommand(id, request.Reason, request.Version), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("RejectProduct")
            .WithTags("Product Workflow")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductStatusChangedResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Reject Product")
            .WithDescription("PendingReview → Rejected (reason required). Needs `catalog.moderate` (Admin, SuperAdmin). Optional `version` → 409 when stale; the response carries the new version.")
            .RequirePermission(Permissions.CatalogModerate)
            .HasApiVersion(1.0);
    }
}
