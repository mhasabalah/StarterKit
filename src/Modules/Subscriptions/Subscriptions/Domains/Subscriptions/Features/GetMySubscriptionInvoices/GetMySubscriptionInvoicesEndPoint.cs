using Subscriptions.Contracts.Subscribers;
namespace Subscriptions.Domains.Subscriptions.Features.GetMySubscriptionInvoices;

public class GetMySubscriptionInvoicesEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/me/invoices",
                async (
                    [AsParameters] GetMySubscriptionInvoicesQuery query,
                    IHttpContextService httpContextService, ISubscriberIdProvider subscribers,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");

                    var result = await sender.Send(query with { SubscriberId = subscriberId }, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetMySubscriptionInvoices")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions", "Invoices")
            .Produces<ApiResponse<PaginatedResult<InvoiceDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithSummary("Get My Subscription Invoices")
            .WithDescription("Returns paginated invoices for the authenticated subscriber's active subscription.")
            .HasApiVersion(1.0);
    }
}
