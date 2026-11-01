using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.RejectRenewalPayment;

public record RejectRenewalPaymentRequest(Guid PaymentId, string Reason);

public class RejectRenewalPaymentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/renewal/reject",
                async (Guid id,
                    RejectRenewalPaymentRequest request,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(
                        new RejectRenewalPaymentCommand(id, request.PaymentId, adminId, request.Reason),
                        ct);
                    return Results.Ok(ApiResponseExtensions.Success("Rejected"));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("RejectRenewalPayment")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
