using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.ReactivateSubscription;

public record ReactivateSubscriptionRequest(string Reason, bool Complimentary = false, int? Months = null, long? Version = null);

public class ReactivateSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/reactivate",
                async (Guid id, ReactivateSubscriptionRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new ReactivateSubscriptionCommand(
                        id, adminId, request.Reason, request.Complimentary, request.Months, request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingApprove)
            .RequireRateLimiting("create")
            .WithName("ReactivateSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<ReactivateSubscriptionResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .WithSummary("Reactivate a cancelled/expired subscription")
            .WithDescription("Within the reactivation window: PendingApproval with a new activation payment, or Active at zero price when complimentary.")
            .HasApiVersion(1.0);
    }
}
