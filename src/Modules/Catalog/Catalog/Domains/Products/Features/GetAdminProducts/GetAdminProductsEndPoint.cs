namespace Catalog.Domains.Products.Features.GetAdminProducts;

public class GetAdminProductsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/products",
                async ([AsParameters] GetAdminProductsQuery query, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(query, cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetAdminProducts")
            .WithTags("Products (Admin)")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<PaginatedResult<ProductAdminDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Get Products (Admin)")
            .WithDescription(
                "All products in any status with reviewer/audit fields and the raw bilingual name. " +
                "`includeDeleted=true` also returns soft-deleted products. Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
