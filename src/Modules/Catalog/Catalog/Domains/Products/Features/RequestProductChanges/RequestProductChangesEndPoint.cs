namespace Catalog.Domains.Products.Features.RequestProductChanges;

public record RequestProductChangesRequest(string Notes, long? Version = null);

public class RequestProductChangesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/request-changes",
                async ([FromRoute] Guid id, [FromBody] RequestProductChangesRequest request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new RequestProductChangesCommand(id, request.Notes, request.Version), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("RequestProductChanges")
            .WithTags("Product Workflow")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductStatusChangedResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Request Product Changes")
            .WithDescription("PendingReview → ChangesRequested (notes required). Needs `catalog.moderate` (Admin, SuperAdmin). Optional `version` → 409 when stale; the response carries the new version.")
            .RequirePermission(Permissions.CatalogModerate)
            .HasApiVersion(1.0);
    }
}
