using Catalog.Contracts.Products.Commands;

namespace Catalog.Domains.Products.Features.UpdateProduct;

public record UpdateProductRequest(
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    bool IsActive,
    Guid? ImageAttachmentId = null,
    long? Version = null);

public record UpdateProductResponse(bool Success);

public class UpdateProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/products/{id:guid}",
                async ([FromRoute] Guid id, [FromBody] UpdateProductRequest request,
                    ISender sender, CancellationToken cancellationToken) =>
                {
                    // The route id is the source of truth — the body never carries the id.
                    var command = new UpdateProductCommand(
                        id,
                        request.Name,
                        request.Description,
                        request.Sku,
                        request.Price,
                        request.IsActive,
                        request.ImageAttachmentId,
                        request.Version);

                    var result = await sender.Send(command, cancellationToken);
                    var response = result.Adapt<UpdateProductResponse>();

                    return Results.Ok(ApiResponseExtensions.Success(response));
                })
            .WithName("UpdateProduct")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<UpdateProductResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update Product")
            .WithDescription(
                "Update a product. Send the `version` from the read DTO to get optimistic " +
                "concurrency protection (stale version returns 409). Admin only.")
            .WithOpenApi()
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
