using Shared.Core.Authorization;

namespace Search.Domains.Platform.Features.SearchPlatform;

/// <summary>
/// Platform operators (permission <c>search.operate</c>, SuperAdmin only): the engine and its definitions, and a rebuild
/// of one index in every tenant. Host-only: these routes never resolve a tenant. Tenants manage their own indexes
/// through <c>/search/indexes</c>.
/// </summary>
public class SearchPlatformModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/admin/search")
            .WithTags("Search")
            .WithApiVersionSet(app.NewApiVersionSet("SearchPlatform").Build())
            .HasApiVersion(1.0);

        group.MapGet("/engine", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetSearchEngineQuery(), ct))))
            .WithName("GetSearchEngine").Produces<ApiResponse<SearchEngineDto>>()
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Search engine")
            .WithDescription("The configured engine (`Search:Engine`) and every registered index definition with its fingerprint.")
            .HostOnly()
            .RequirePermission(Permissions.SearchOperate);

        group.MapPost("/indexes/{key}/rebuild-all", async (string key, ISender sender, CancellationToken ct) =>
                Results.Accepted(value: ApiResponseExtensions.Success(
                    await sender.Send(new RebuildSearchIndexInAllTenantsCommand(key), ct))))
            .WithName("RebuildSearchIndexInAllTenants")
            .Produces<ApiResponse<RebuildSearchIndexInAllTenantsResult>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Rebuild a search index in every tenant")
            .WithDescription("Queues one full rebuild per tenant on the `search` queue; each tenant keeps its current build " +
                             "until its new one completes. For an engine change or a repair after an incident.")
            .HostOnly()
            .RequirePermission(Permissions.SearchOperate)
            .DisallowImpersonation();
    }
}
