namespace Subscriptions.Domains.Subscriptions.Features.GetAdminSubscriptions;

public class GetAdminSubscriptionsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/subscriptions",
                async ([AsParameters] GetAdminSubscriptionsQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingRead)
            .RequireRateLimiting("search")
            .WithName("GetAdminSubscriptions")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<PaginatedResult<AdminSubscriptionListItemDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Search subscriptions")
            .WithDescription("Paged admin search: status, plan, currency, subscriber text (name/email) or id, renewal date range, price range, sort.")
            .HasApiVersion(1.0);
    }
}
