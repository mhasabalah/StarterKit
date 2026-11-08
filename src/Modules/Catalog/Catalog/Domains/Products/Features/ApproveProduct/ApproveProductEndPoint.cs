namespace Catalog.Domains.Products.Features.ApproveProduct;

public record ApproveProductRequest(string? ReviewNotes = null, long? Version = null);

public class ApproveProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/approve",
                async ([FromRoute] Guid id, [FromBody] ApproveProductRequest? request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new ApproveProductCommand(id, request?.ReviewNotes, request?.Version), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("ApproveProduct")
            .WithTags("Product Workflow")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductStatusChangedResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Approve Product")
            .WithDescription("PendingReview → Approved. Needs `catalog.moderate` (Admin, SuperAdmin). Optional `version` → 409 when stale; the response carries the new version.")
            .RequirePermission(Permissions.CatalogModerate)
            .HasApiVersion(1.0);
    }
}
