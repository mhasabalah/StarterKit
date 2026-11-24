using Shared.Core.MultiTenancy.Authorization;

namespace Search.Domains.Analytics.Features.SearchAnalytics;

/// <summary>
/// Search analytics of the current tenant (tenant permission <c>search.manage</c>): what people searched for and did not
/// find, and the daily counts. Nothing here identifies who searched.
/// </summary>
public class SearchAnalyticsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/search/analytics")
            .WithTags("Search")
            .WithApiVersionSet(app.NewApiVersionSet("SearchAnalytics").Build())
            .HasApiVersion(1.0);

        group.MapGet("/zero-results", async ([AsParameters] GetSearchZeroResultTermsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetSearchZeroResultTerms").Produces<ApiResponse<PaginatedResult<SearchZeroResultTermDto>>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .WithSummary("Searches that found nothing")
            .WithDescription("Normalized search terms that returned no results, most frequent first, over `from`..`to` " +
                             "(UTC days, default the last 30). Filter by `indexKey`. Terms that look like an email address " +
                             "or a long number are never kept.")
            .RequireTenantPermission(TenantPermissions.SearchManage);

        group.MapGet("/daily", async ([AsParameters] GetSearchDailyStatsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetSearchDailyStats").Produces<ApiResponse<List<SearchDailyStatsDto>>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .WithSummary("Daily search counts")
            .WithDescription("Per index and UTC day: searches, zero results, close-spelling rescues and searches the index " +
                             "could not answer, over `from`..`to` (default the last 30 days, at most 366).")
            .RequireTenantPermission(TenantPermissions.SearchManage);
    }
}
