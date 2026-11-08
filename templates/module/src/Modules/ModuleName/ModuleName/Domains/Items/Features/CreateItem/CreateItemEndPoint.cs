namespace ModuleName.Domains.Items.Features.CreateItem;

public record CreateItemRequest(string Code, string Name, string? Description);

public class CreateItemEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(ItemRoutes.Base,
                async (CreateItemRequest request, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new CreateItemCommand(request.Code, request.Name, request.Description), cancellationToken);
                    return Results.Created($"{ItemRoutes.Base}/{result.Id}", ApiResponseExtensions.Success(result));
                })
            .WithName("CreateModuleNameItem")
            .WithTags(ItemRoutes.Tag)
            .WithApiVersionSet(app.NewApiVersionSet(ItemRoutes.Tag).Build())
            .Produces<ApiResponse<CreateItemResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create Item")
            .WithDescription("Create an item. The code is unique (case-insensitive) and immutable.")
            .RequireItemsAccess(write: true)
            .HasApiVersion(1.0);
    }
}
