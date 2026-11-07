namespace ModuleName.Domains.Items.Features.UpdateItem;

public record UpdateItemRequest(string Name, string? Description, long? Version = null);

public class UpdateItemEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(ItemRoutes.Base + "/{id:guid}",
                async ([FromRoute] Guid id, [FromBody] UpdateItemRequest request, ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    // The route id is the source of truth; the body never carries it.
                    var result = await sender.Send(
                        new UpdateItemCommand(id, request.Name, request.Description, request.Version), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("UpdateModuleNameItem")
            .WithTags(ItemRoutes.Tag)
            .WithApiVersionSet(app.NewApiVersionSet(ItemRoutes.Tag).Build())
            .Produces<ApiResponse<UpdateItemResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update Item")
            .WithDescription("Rename or redescribe an item. Send `version` from the read DTO: a stale one gives 409.")
            .RequireItemsAccess(write: true)
            .HasApiVersion(1.0);
    }
}
