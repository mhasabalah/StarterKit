namespace Catalog.Domains.AttributeTypes.Features;

public record UpdateAttributeTypeRequest(
    LocalizedText Name,
    AttributeDataType DataType,
    string? Unit = null,
    ProductType[]? ApplicableTypes = null,
    bool IsActive = true,
    long? Version = null);

public class AttributeTypesModule : ICarterModule
{
    private const string Tag = "Attribute Types";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Catalog").Build();

        app.MapGet(EndpointConfig.BaseApiPath + "/attribute-types",
                async ([AsParameters] GetAttributeTypesQuery query, HttpContext http, ISender sender,
                    CancellationToken ct) =>
                {
                    var effective = query with { IncludeInactive = query.IncludeInactive && LookupAccess.IsAdmin(http) };
                    return Results.Ok(ApiResponseExtensions.Success(await sender.Send(effective, ct)));
                })
            .WithName("GetAttributeTypes").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<List<AttributeTypeDto>>>(StatusCodes.Status200OK)
            .WithSummary("Get Attribute Types")
            .WithDescription(
                "Active attribute types, optionally only those applicable to `productType`. " +
                "`includeInactive=true` is honoured for admins only. Public.")
            .AllowAnonymous()
            .HasApiVersion(1.0);

        app.MapPost(EndpointConfig.BaseApiPath + "/attribute-types",
                async (CreateAttributeTypeCommand command, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(command, ct);
                    return Results.Created($"api/v1/attribute-types/{result.Id}", ApiResponseExtensions.Success(result));
                })
            .WithName("CreateAttributeType").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<CreateAttributeTypeResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create Attribute Type").WithDescription("Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);

        app.MapPut(EndpointConfig.BaseApiPath + "/attribute-types/{id:guid}",
                async ([FromRoute] Guid id, [FromBody] UpdateAttributeTypeRequest request, ISender sender,
                    CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new UpdateAttributeTypeCommand(
                        id, request.Name, request.DataType, request.Unit, request.ApplicableTypes, request.IsActive,
                        request.Version), ct))))
            .WithName("UpdateAttributeType").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<UpdateAttributeTypeResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update Attribute Type").WithDescription("Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);

        app.MapDelete(EndpointConfig.BaseApiPath + "/attribute-types/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteAttributeTypeCommand(id), ct))))
            .WithName("DeleteAttributeType").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<DeleteAttributeTypeResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Deactivate Attribute Type").WithDescription("Deactivates (never deletes). Admin only.")
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .HasApiVersion(1.0);
    }
}
