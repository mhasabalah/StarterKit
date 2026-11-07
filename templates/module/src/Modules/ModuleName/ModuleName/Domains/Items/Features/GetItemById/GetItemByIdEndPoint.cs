namespace ModuleName.Domains.Items.Features.GetItemById;

public class GetItemByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(ItemRoutes.Base + "/{id:guid}",
                async ([FromRoute] Guid id, ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        await sender.Send(new GetItemByIdQuery(id), cancellationToken))))
            .WithName("GetModuleNameItemById")
            .WithTags(ItemRoutes.Tag)
            .WithApiVersionSet(app.NewApiVersionSet(ItemRoutes.Tag).Build())
            .Produces<ApiResponse<ItemDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Get Item By Id")
            .WithDescription("One item, with the `version` to send back on update.")
            .RequireItemsAccess(write: false)
            .HasApiVersion(1.0);
    }
}
