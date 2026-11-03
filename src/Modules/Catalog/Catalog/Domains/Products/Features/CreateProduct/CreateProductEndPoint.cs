namespace Catalog.Domains.Products.Features.CreateProduct;

public record CreateProductRequest(
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
    Guid? AttachmentSessionId = null,
    Guid? OwnerId = null);

public record CreateProductResponse(Guid Id, bool ImagesAssociated);

public class CreateProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products",
                async (CreateProductRequest request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var command = new CreateProductCommand(
                        request.Name, request.Description, request.Sku, request.Type, request.Price,
                        request.CompareAtPrice, request.Currency, request.CategoryId, request.TagIds,
                        request.Attributes, request.AttachmentSessionId, request.OwnerId);

                    var result = await sender.Send(command, cancellationToken);
                    var response = new CreateProductResponse(result.Id, result.ImagesAssociated);

                    return Results.Created($"api/v1/products/{response.Id}", ApiResponseExtensions.Success(response));
                })
            .WithName("CreateProduct")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<CreateProductResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status429TooManyRequests)
            .WithSummary("Create Product")
            .WithDescription(
                "Create a Draft product owned by the caller (admins may pass `ownerId`). SKU must be unique. " +
                "Pass `attachmentSessionId` to turn pending uploads into the gallery. Any authenticated user.")
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
