namespace ModuleName.Domains.Items.Features.DeleteItem;

public class DeleteItemEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(ItemRoutes.Base + "/{id:guid}",
                async ([FromRoute] Guid id, ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        await sender.Send(new DeleteItemCommand(id), cancellationToken))))
            .WithName("DeleteModuleNameItem")
            .WithTags(ItemRoutes.Tag)
            .WithApiVersionSet(app.NewApiVersionSet(ItemRoutes.Tag).Build())
            .Produces<ApiResponse<DeleteItemResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete Item")
            .WithDescription("Delete an item.")
            .RequireItemsAccess(write: true)
            .DisallowImpersonation()
            .HasApiVersion(1.0);
    }
}
