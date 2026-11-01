using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetPaymentHistory;

public class GetPaymentHistoryEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/me/payments",
                async (
                    [AsParameters] GetPaymentHistoryQuery query,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");

                    var result = await sender.Send(query with { SubscriberId = subscriberId }, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetPaymentHistory")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<PaginatedResult<PaymentHistoryDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithSummary("Get Payment History")
            .WithDescription("Retrieve paginated payment history for the authenticated subscriber.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
