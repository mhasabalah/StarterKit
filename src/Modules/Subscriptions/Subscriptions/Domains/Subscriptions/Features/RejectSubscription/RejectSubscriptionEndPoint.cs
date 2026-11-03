using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.RejectSubscription;

public record RejectSubscriptionRequest(string? Reason, Guid? PaymentId = null, long? Version = null);

public class RejectSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/reject",
                async (Guid id, RejectSubscriptionRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new RejectSubscriptionCommand(id, adminId, request.Reason, request.PaymentId, request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Rejected"));
                })
            .RequirePermission(Permissions.BillingApprove)
            .WithName("RejectSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
