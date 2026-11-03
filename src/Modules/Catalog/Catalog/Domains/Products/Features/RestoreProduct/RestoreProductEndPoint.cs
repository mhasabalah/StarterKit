namespace Catalog.Domains.Products.Features.RestoreProduct;

public class RestoreProductEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/products/{id:guid}/restore",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new RestoreProductCommand(id), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("RestoreProduct")
            .WithTags("Products (Admin)")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<RestoreProductResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Restore Product")
            .WithDescription("Undo a soft delete. Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
