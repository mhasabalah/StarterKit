namespace Catalog.Domains.Products.Features.SetPrimaryProductImage;

public class SetPrimaryProductImageEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/products/{id:guid}/images/{imageId:guid}/set-primary",
                async (Guid id, Guid imageId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new SetPrimaryProductImageCommand(id, imageId), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("SetPrimaryProductImage")
            .WithTags("Product Images")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<SetPrimaryProductImageResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Set Primary Product Image")
            .WithDescription("Make one gallery image the primary image (exactly one primary). Owner or admin.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
