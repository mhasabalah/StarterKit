namespace Catalog.Domains.Products.Features.UnarchiveProduct;

public class UnarchiveProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/unarchive",
                async ([FromRoute] Guid id, [FromBody] ProductWorkflowRequest? request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new UnarchiveProductCommand(id, request?.Version), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("UnarchiveProduct")
            .WithTags("Product Workflow")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductStatusChangedResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Unarchive Product")
            .WithDescription("Archived → Draft, so the owner can rework and resubmit. Owner or admin. Optional `version` → 409 when stale; the response carries the new version.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
