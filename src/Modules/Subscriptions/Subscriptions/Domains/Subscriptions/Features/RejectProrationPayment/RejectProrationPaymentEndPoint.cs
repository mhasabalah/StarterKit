using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.RejectProrationPayment;

/// <param name="Version">The PAYMENT version (PendingPaymentDto.Version).</param>
public record RejectProrationPaymentRequest(Guid PaymentId, string Reason, long? Version = null);

public class RejectProrationPaymentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/proration/reject",
                async (Guid id, RejectProrationPaymentRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(
                        new RejectProrationPaymentCommand(id, request.PaymentId, adminId, request.Reason, request.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Rejected"));
                })
            .RequirePermission(Permissions.BillingApprove)
            .WithName("RejectProrationPayment")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
