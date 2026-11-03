namespace Catalog.Domains.Products.Features.ArchiveProduct;

public class ArchiveProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/archive",
                async ([FromRoute] Guid id, [FromBody] ProductWorkflowRequest? request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new ArchiveProductCommand(id, request?.Version), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("ArchiveProduct")
            .WithTags("Product Workflow")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductStatusChangedResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Archive Product")
            .WithDescription("Published → Archived (taken off sale). Owner or admin. Optional `version` → 409 when stale; the response carries the new version.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
