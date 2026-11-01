using Catalog.Contracts.Products.Commands;

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
                    var response = result.Adapt<DeleteProductResponse>();

                    return Results.Ok(ApiResponseExtensions.Success(response));
                })
            .WithName("DeleteProduct")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<DeleteProductResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete Product")
            .WithDescription("Delete a product. Admin only.")
            .WithOpenApi()
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
