namespace Catalog.Domains.Products.Features.GetAdminProductById;

public class GetAdminProductByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/products/{id:guid}",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetAdminProductByIdQuery(id), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result.Product));
                })
            .WithName("GetAdminProductById")
            .WithTags("Products (Admin)")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<ProductAdminDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Get Product By ID (Admin)")
            .WithDescription("Full product detail incl. review history, audit fields and soft-deleted products. Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
