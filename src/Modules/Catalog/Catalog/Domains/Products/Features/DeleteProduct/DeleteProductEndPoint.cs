namespace Catalog.Domains.Products.Features.DeleteProduct;

public record DeleteProductResponse(bool Success);

public class DeleteProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/products/{id:guid}",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new DeleteProductCommand(id), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(new DeleteProductResponse(result.Success)));
                })
            .WithName("DeleteProduct")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<DeleteProductResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete Product")
            .WithDescription("Soft-delete a product (owner or admin). Admins can restore it until it is purged.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
