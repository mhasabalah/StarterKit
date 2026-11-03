namespace Catalog.Domains.Products.Features.ToggleProductFeatured;

public record ToggleProductFeaturedRequest(bool IsFeatured);

public class ToggleProductFeaturedEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch(EndpointConfig.BaseApiPath + "/admin/products/{id:guid}/featured",
                async ([FromRoute] Guid id, [FromBody] ToggleProductFeaturedRequest request, ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    await sender.Send(new ToggleProductFeaturedCommand(id, request.IsFeatured), cancellationToken);
                    return Results.NoContent();
                })
            .WithName("ToggleProductFeatured")
            .WithTags("Products (Admin)")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Toggle Product Featured")
            .WithDescription("Feature or unfeature a published product. Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
