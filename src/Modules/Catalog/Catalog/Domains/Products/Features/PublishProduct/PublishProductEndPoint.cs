namespace Catalog.Domains.Products.Features.PublishProduct;

public class PublishProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/publish",
                async ([FromRoute] Guid id, [FromBody] ProductWorkflowRequest? request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new PublishProductCommand(id, request?.Version), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("PublishProduct")
            .WithTags("Product Workflow")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductStatusChangedResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Publish Product")
            .WithDescription("Approved → Published (publicly visible; raises ProductPublishedIntegrationEvent). Needs `catalog.moderate` (Admin, SuperAdmin). Optional `version` → 409 when stale; the response carries the new version.")
            .RequirePermission(Permissions.CatalogModerate)
            .HasApiVersion(1.0);
    }
}
