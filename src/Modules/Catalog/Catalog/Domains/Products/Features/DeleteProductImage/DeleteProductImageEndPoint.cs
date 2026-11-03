namespace Catalog.Domains.Products.Features.DeleteProductImage;

public class DeleteProductImageEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/products/{id:guid}/images/{imageId:guid}",
                async (Guid id, Guid imageId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new DeleteProductImageCommand(id, imageId), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("DeleteProductImage")
            .WithTags("Product Images")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<DeleteProductImageResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete Product Image")
            .WithDescription("Remove an image from the gallery and delete its file. Owner (editable status) or admin.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
