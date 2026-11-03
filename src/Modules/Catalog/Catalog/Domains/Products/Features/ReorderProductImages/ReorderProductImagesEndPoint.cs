namespace Catalog.Domains.Products.Features.ReorderProductImages;

public record ReorderProductImagesRequest(List<Guid> OrderedImageIds);

public class ReorderProductImagesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/products/{id:guid}/images/reorder",
                async ([FromRoute] Guid id, [FromBody] ReorderProductImagesRequest request, ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new ReorderProductImagesCommand(id, request.OrderedImageIds), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("ReorderProductImages")
            .WithTags("Product Images")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ReorderProductImagesResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Reorder Product Images")
            .WithDescription("Send every image id of the product in the new display order. Owner or admin.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
