namespace Catalog.Domains.Products.Features.UpdateProduct;

public record UpdateProductRequest(
    LocalizedText Name,
    string? Description,
    string Sku,
    ProductType Type,
    decimal Price,
    decimal? CompareAtPrice,
    Currency Currency,
    Guid? CategoryId = null,
    List<Guid>? TagIds = null,
    List<ProductAttributeInput>? Attributes = null,
    long? Version = null);

public record UpdateProductResponse(bool Success, long Version);

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
                        id, request.Name, request.Description, request.Sku, request.Type, request.Price,
                        request.CompareAtPrice, request.Currency, request.CategoryId, request.TagIds,
                        request.Attributes, request.Version);

                    var result = await sender.Send(command, cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(
                        new UpdateProductResponse(result.Success, result.Version)));
                })
            .WithName("UpdateProduct")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<UpdateProductResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update Product")
            .WithDescription(
                "Update a product. Owners may edit only Draft / ChangesRequested / Rejected products; admins " +
                "any. Send `version` from the read DTO for optimistic concurrency (stale version → 409).")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
