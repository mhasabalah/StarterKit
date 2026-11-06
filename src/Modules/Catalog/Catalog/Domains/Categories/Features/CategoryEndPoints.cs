using Shared.Core.MultiTenancy.Authorization;

namespace Catalog.Domains.Categories.Features;

// Lookup admin CRUD grouped in one Carter module per CLAUDE.md naming ("grouped route modules: <X>Module").
// Writes need catalog.categories.manage (tenant data); the tree is public and cached.

public record UpdateCategoryRequest(
    LocalizedText Name,
    string Slug,
    Guid? ParentId,
    int DisplayOrder,
    bool IsActive = true,
    long? Version = null);

public class CategoriesModule : ICarterModule
{
    private const string Tag = "Categories";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Catalog").Build();

        app.MapGet(EndpointConfig.BaseApiPath + "/categories/tree",
                async (ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetCategoryTreeQuery(), ct))))
            .WithName("GetCategoryTree").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<List<CategoryTreeNodeDto>>>(StatusCodes.Status200OK)
            .WithSummary("Get Category Tree")
            .WithDescription("Active categories as a localized tree (cached in Redis). Public.")
            .AllowAnonymous()
            .HasApiVersion(1.0);

        app.MapGet(EndpointConfig.BaseApiPath + "/categories",
                async ([AsParameters] GetCategoriesQuery query, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetCategories").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<List<CategoryDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Get Categories")
            .WithDescription("Flat list with raw bilingual names; `includeInactive=true` for deactivated ones. Requires `catalog.categories.manage`.")
            .RequireTenantPermission(TenantPermissions.CategoriesManage)
            .HasApiVersion(1.0);

        app.MapPost(EndpointConfig.BaseApiPath + "/categories",
                async (CreateCategoryCommand command, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(command, ct);
                    return Results.Created($"api/v1/categories/{result.Id}", ApiResponseExtensions.Success(result));
                })
            .WithName("CreateCategory").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<CreateCategoryResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create Category")
            .WithDescription("Create a category (optional parent). Slug must be unique. Requires `catalog.categories.manage`.")
            .RequireTenantPermission(TenantPermissions.CategoriesManage)
            .HasApiVersion(1.0);

        app.MapPut(EndpointConfig.BaseApiPath + "/categories/{id:guid}",
                async ([FromRoute] Guid id, [FromBody] UpdateCategoryRequest request, ISender sender,
                    CancellationToken ct) =>
                {
                    var result = await sender.Send(new UpdateCategoryCommand(
                        id, request.Name, request.Slug, request.ParentId, request.DisplayOrder, request.IsActive,
                        request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("UpdateCategory").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<UpdateCategoryResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Update Category")
            .WithDescription("Rename / move / (de)activate. Moving under a descendant is rejected. Requires `catalog.categories.manage`.")
            .RequireTenantPermission(TenantPermissions.CategoriesManage)
            .HasApiVersion(1.0);

        app.MapDelete(EndpointConfig.BaseApiPath + "/categories/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteCategoryCommand(id), ct))))
            .WithName("DeleteCategory").WithTags(Tag).WithApiVersionSet(versionSet)
            .Produces<ApiResponse<DeleteCategoryResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Deactivate Category")
            .WithDescription("Deactivates (never deletes) the category. Requires `catalog.categories.manage`.")
            .RequireTenantPermission(TenantPermissions.CategoriesManage)
            .HasApiVersion(1.0);
    }
}
