namespace Catalog.Domains.Wishlist.Features;

public class WishlistModule : ICarterModule
{
    private const string Tag = "Wishlist";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Catalog").Build();

        app.MapGet(EndpointConfig.BaseApiPath + "/products/wishlist",
                async ([AsParameters] GetWishlistQuery query, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetWishlist").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<PaginatedResult<WishlistItemDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithSummary("Get My Wishlist")
            .WithDescription("The caller's saved products, newest first. Requires authentication.")
            .RequireAuthorization()
            .HasApiVersion(1.0);

        app.MapPost(EndpointConfig.BaseApiPath + "/products/{id:guid}/wishlist",
                async (Guid id, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new AddToWishlistCommand(id), ct))))
            .WithName("AddToWishlist").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<AddToWishlistResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Add To Wishlist")
            .WithDescription("Save a published product. Saving it twice returns 409. Requires authentication.")
            .RequireAuthorization()
            .HasApiVersion(1.0);

        app.MapDelete(EndpointConfig.BaseApiPath + "/products/{id:guid}/wishlist",
                async (Guid id, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RemoveFromWishlistCommand(id), ct))))
            .WithName("RemoveFromWishlist").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<RemoveFromWishlistResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Remove From Wishlist")
            .WithDescription("Remove a saved product. Requires authentication.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
