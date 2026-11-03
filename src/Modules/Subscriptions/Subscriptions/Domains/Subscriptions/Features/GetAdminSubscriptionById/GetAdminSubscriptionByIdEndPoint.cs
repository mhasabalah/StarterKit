namespace Subscriptions.Domains.Subscriptions.Features.GetAdminSubscriptionById;

public class GetAdminSubscriptionByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GetAdminSubscriptionByIdQuery(id), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingRead)
            .RequireRateLimiting("search")
            .WithName("GetAdminSubscriptionById")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<AdminSubscriptionDetailsDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .WithSummary("Subscriber-360")
            .WithDescription("Subscription with payments (receipts, proof hash, duplicate flag), invoices, credit notes, refunds, transition log, scheduled changes, ledger and plan limits.")
            .HasApiVersion(1.0);
    }
}
