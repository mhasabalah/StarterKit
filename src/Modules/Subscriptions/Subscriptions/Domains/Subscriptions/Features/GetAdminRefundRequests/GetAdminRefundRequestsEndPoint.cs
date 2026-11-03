using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetAdminRefundRequests;

public class GetAdminRefundRequestsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/refunds",
                async ([AsParameters] GetAdminRefundRequestsQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.BillingRead)
            .WithName("GetAdminRefundRequests")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<PaginatedResult<AdminRefundRequestDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get Admin Refund Requests")
            .WithDescription("Retrieve paginated refund requests, optionally filtered by status. Admin only.")
            .HasApiVersion(1.0);
    }
}
