using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.OnlinePayments.Features.GetFailedWebhookEvents;

public class GetFailedWebhookEventsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/subscriptions/webhook-events/failed",
                async ([AsParameters] GetFailedWebhookEventsQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingRead)
            .RequireRateLimiting("search")
            .WithName("GetFailedWebhookEvents")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<PaginatedResult<InboundWebhookEventDto>>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get failed payment webhook events")
            .WithDescription("Verified provider webhooks that could not be applied (amount mismatch, unknown reference, already settled, ...). Requires billing.read.")
            .HasApiVersion(1.0);
    }
}
