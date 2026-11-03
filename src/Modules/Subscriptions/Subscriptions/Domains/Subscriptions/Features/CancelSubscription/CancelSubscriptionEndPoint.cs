using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Domains.Subscriptions.Features.CancelSubscription;

public record CancelSubscriptionRequest(
    string? Reason,
    CancellationReason? CancellationReason = null,
    string? CancellationNote = null);

public class CancelSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/cancel",
                async (Guid id, CancelSubscriptionRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var actorId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new CancelSubscriptionCommand(id, actorId, false, request.Reason, request.CancellationReason, request.CancellationNote), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Cancellation scheduled for end of billing period."));
                })
            .RequireAuthorization()
            .WithName("CancelSubscriptionBySubscriber")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);

        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/cancel",
                async (Guid id, CancelSubscriptionRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var actorId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new CancelSubscriptionCommand(id, actorId, true, request.Reason, request.CancellationReason, request.CancellationNote), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Cancelled"));
                })
            .RequirePermission(Permissions.BillingApprove)
            .WithName("CancelSubscriptionByAdmin")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
