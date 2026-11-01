using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.AdminReviewRefund;

public record AdminReviewRefundRequest(bool Approve, string? Notes);

public class AdminReviewRefundEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/refunds/{refundId:guid}/review",
                async (Guid refundId, AdminReviewRefundRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new AdminReviewRefundCommand(refundId, adminId, request.Approve, request.Notes), ct);
                    return Results.Ok(ApiResponseExtensions.Success(request.Approve ? "Refund approved." : "Refund rejected."));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("AdminReviewRefund")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
