using Shared.Core.MultiTenancy.Authorization;

namespace Search.Domains.Indexes.Features.SearchIndexes;

/// <summary>
/// The current tenant's search indexes: their build state, a rebuild and a drift check. Every route needs the tenant
/// permission <c>search.manage</c>; the actions run on the <c>search</c> queue (202 Accepted) and are not available while
/// a platform operator impersonates a user. Searching is decided by the owning modules' own endpoints.
/// </summary>
public class SearchIndexesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/search/indexes")
            .WithTags("Search")
            .WithApiVersionSet(app.NewApiVersionSet("Search").Build())
            .HasApiVersion(1.0);

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetSearchIndexesQuery(), ct))))
            .WithName("GetSearchIndexes").Produces<ApiResponse<List<SearchIndexDto>>>()
            .WithSummary("Search indexes")
            .WithDescription("Every registered index with its build state in the current tenant (NotBuilt until the first build).")
            .RequireTenantPermission(TenantPermissions.SearchManage);

        group.MapGet("/{key}", async (string key, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetSearchIndexByKeyQuery(key), ct))))
            .WithName("GetSearchIndexByKey").Produces<ApiResponse<SearchIndexDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Search index")
            .WithDescription("Build state, the last drift check and the fields of one index, e.g. `catalog.products`.")
            .RequireTenantPermission(TenantPermissions.SearchManage);

        group.MapPost("/{key}/rebuild", async (string key, ISender sender, CancellationToken ct) =>
                Results.Accepted(value: ApiResponseExtensions.Success(await sender.Send(new RebuildSearchIndexCommand(key), ct))))
            .WithName("RebuildSearchIndex").Produces<ApiResponse<RebuildSearchIndexResult>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Rebuild a search index")
            .WithDescription("Queues a full rebuild from the owning module's data. The current build keeps answering until " +
                             "the new one completes. 409 `search_rebuild_running` while a rebuild is in progress.")
            .RequireTenantPermission(TenantPermissions.SearchManage)
            .DisallowImpersonation();

        group.MapPost("/{key}/drift-check", async (string key, ISender sender, CancellationToken ct) =>
                Results.Accepted(value: ApiResponseExtensions.Success(await sender.Send(new CheckSearchIndexDriftCommand(key), ct))))
            .WithName("CheckSearchIndexDrift").Produces<ApiResponse<CheckSearchIndexDriftResult>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Check a search index for drift")
            .WithDescription("Queues a comparison of the index with its source; the findings appear on the index (`drift`). " +
                             "409 `search_index_not_built` before the first build.")
            .RequireTenantPermission(TenantPermissions.SearchManage)
            .DisallowImpersonation();
    }
}
