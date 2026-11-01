using Catalog.Contracts.Products.Commands;

namespace Catalog.Domains.Products.Features.CreateProduct;

public record CreateProductRequest(
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    bool IsActive = true,
    Guid? ImageAttachmentId = null);

public record CreateProductResponse(Guid Id);

public class CreateProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/products",
                async (CreateProductRequest request, ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var command = request.Adapt<CreateProductCommand>();
                    var result = await sender.Send(command, cancellationToken);
                    var response = result.Adapt<CreateProductResponse>();

                    return Results.Created($"api/v1/products/{response.Id}",
                        ApiResponseExtensions.Success(response));
                })
            .WithName("CreateProduct")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<CreateProductResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create Product")
            .WithDescription("Create a catalog product. SKU must be unique. Admin only.")
            .WithOpenApi()
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
