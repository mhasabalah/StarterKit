using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetSubscriptionInvoices;

public class GetSubscriptionInvoicesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/invoices",
                async (
                    Guid id,
                    [AsParameters] GetSubscriptionInvoicesQuery query,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");

                    var result = await sender.Send(
                        query with { SubscriptionId = id, SubscriberId = subscriberId }, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetSubscriptionInvoices")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions", "Invoices")
            .Produces<ApiResponse<PaginatedResult<InvoiceDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithSummary("Get Subscription Invoices")
            .WithDescription("Retrieve paginated invoices for a specific subscription owned by the authenticated subscriber.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
