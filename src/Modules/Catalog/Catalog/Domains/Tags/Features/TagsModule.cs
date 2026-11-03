namespace Catalog.Domains.Tags.Features;

public record UpdateTagRequest(LocalizedText Name, bool IsActive = true, long? Version = null);

/// <summary>Grouped lookup routes. GET is public (inactive tags only for admins); writes are AdminOnly.</summary>
public class TagsModule : ICarterModule
{
    private const string Tag = "Tags";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Catalog").Build();

        app.MapGet(EndpointConfig.BaseApiPath + "/tags",
                async ([AsParameters] GetTagsQuery query, HttpContext http, ISender sender, CancellationToken ct) =>
                {
                    var effective = query with { IncludeInactive = query.IncludeInactive && LookupAccess.IsAdmin(http) };
                    return Results.Ok(ApiResponseExtensions.Success(await sender.Send(effective, ct)));
                })
            .WithName("GetTags").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<List<TagDto>>>(StatusCodes.Status200OK)
            .WithSummary("Get Tags")
            .WithDescription("Active tags. `includeInactive=true` is honoured for admins only. Public.")
            .AllowAnonymous()
            .HasApiVersion(1.0);

        app.MapPost(EndpointConfig.BaseApiPath + "/tags",
                async (CreateTagCommand command, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(command, ct);
                    return Results.Created($"api/v1/tags/{result.Id}", ApiResponseExtensions.Success(result));
                })
            .WithName("CreateTag").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<CreateTagResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create Tag").WithDescription("Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);

        app.MapPut(EndpointConfig.BaseApiPath + "/tags/{id:guid}",
                async ([FromRoute] Guid id, [FromBody] UpdateTagRequest request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                        new UpdateTagCommand(id, request.Name, request.IsActive, request.Version), ct))))
            .WithName("UpdateTag").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<UpdateTagResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update Tag").WithDescription("Rename / (de)activate. Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);

        app.MapDelete(EndpointConfig.BaseApiPath + "/tags/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteTagCommand(id), ct))))
            .WithName("DeleteTag").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<DeleteTagResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Deactivate Tag").WithDescription("Deactivates (never deletes). Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
