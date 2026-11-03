using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.ExtendSubscription;

public record ExtendSubscriptionRequest(int Days, string Reason, long? Version = null);

public class ExtendSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/extend",
                async (Guid id, ExtendSubscriptionRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(
                        new ExtendSubscriptionCommand(id, adminId, request.Days, request.Reason, request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingApprove)
            .RequireRateLimiting("create")
            .WithName("ExtendSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<ExtendSubscriptionResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .WithSummary("Extend a subscription")
            .WithDescription("Active: shifts the renewal date by N days. PastDue: extends the grace period. Audited.")
            .HasApiVersion(1.0);
    }
}
