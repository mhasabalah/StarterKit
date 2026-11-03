using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveRenewal;

/// <summary>Optional body: which renewal payment is approved and the subscription version the admin saw.</summary>
public record ApproveRenewalRequest(Guid? PaymentId = null, long? Version = null);

public class ApproveRenewalEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/subscriptions/{id:guid}/renewal/approve",
                async (Guid id, ApproveRenewalRequest? request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(
                        new ApproveRenewalCommand(id, adminId, request?.PaymentId, request?.Version), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingApprove)
            .RequireRateLimiting("create")
            .WithName("ApproveRenewal")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<ApproveRenewalResult>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
