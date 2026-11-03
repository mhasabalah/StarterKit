using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveSubscription;

/// <summary>Optional body: which payment is approved and the subscription version the admin saw.</summary>
public record ApproveSubscriptionRequest(Guid? PaymentId = null, long? Version = null);

public class ApproveSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/approve",
                async (Guid id, ApproveSubscriptionRequest? request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(
                        new ApproveSubscriptionCommand(id, adminId, request?.PaymentId, request?.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingApprove)
            .RequireRateLimiting("create")
            .WithName("ApproveSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<ApproveSubscriptionResult>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
