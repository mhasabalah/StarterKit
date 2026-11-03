using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.CompSubscription;

public record CompSubscriptionRequest(Guid PlanId, int Months, string Reason, long? Version = null);

public class CompSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/comp",
                async (Guid id, CompSubscriptionRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new CompSubscriptionCommand(
                        id, adminId, request.PlanId, request.Months, request.Reason, request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingApprove)
            .RequireRateLimiting("create")
            .WithName("CompSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<CompSubscriptionResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .WithSummary("Complimentary activation")
            .WithDescription("Activates a pending (or recently cancelled/expired) subscription on a plan for N months at zero price: invoice with 0 total, ledger adjustment 0. Audited.")
            .HasApiVersion(1.0);
    }
}
