using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.MarkRefundProcessed;

public record MarkRefundProcessedRequest(string ExternalTransactionRef);

public class MarkRefundProcessedEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/refunds/{refundId:guid}/mark-processed",
                async (Guid refundId, MarkRefundProcessedRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new MarkRefundProcessedCommand(refundId, adminId, request.ExternalTransactionRef), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Refund marked as processed."));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("MarkRefundProcessed")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<string>>(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
