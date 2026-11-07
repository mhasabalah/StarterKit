namespace ModuleName.Domains.Items.Features.GetItems;

public class GetItemsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(ItemRoutes.Base,
                async ([AsParameters] GetItemsQuery query, ISender sender, CancellationToken cancellationToken) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, cancellationToken))))
            .WithName("GetModuleNameItems")
            .WithTags(ItemRoutes.Tag)
            .WithApiVersionSet(app.NewApiVersionSet(ItemRoutes.Tag).Build())
            .Produces<ApiResponse<PaginatedResult<ItemDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Get Items")
            .WithDescription("Paged items, newest first. `search` matches the start of the code or part of the name.")
            .RequireItemsAccess(write: false)
            .HasApiVersion(1.0);
    }
}
