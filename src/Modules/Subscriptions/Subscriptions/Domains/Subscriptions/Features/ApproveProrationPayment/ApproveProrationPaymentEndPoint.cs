using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveProrationPayment;

/// <param name="Version">The PAYMENT version (PendingPaymentDto.Version).</param>
public record ApproveProrationPaymentRequest(Guid PaymentId, long? Version = null);

public class ApproveProrationPaymentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/proration/approve",
                async (Guid id, ApproveProrationPaymentRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(
                        new ApproveProrationPaymentCommand(id, request.PaymentId, adminId, request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingApprove)
            .RequireRateLimiting("create")
            .WithName("ApproveProrationPayment")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<ApproveProrationPaymentResult>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
