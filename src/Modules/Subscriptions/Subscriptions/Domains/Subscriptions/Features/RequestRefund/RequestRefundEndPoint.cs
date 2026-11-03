using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.RequestRefund;

public class RequestRefundEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/refund",
                async (Guid id, IHttpContextService httpContextService, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var refundId = await sender.Send(new RequestRefundCommand(id, subscriberId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(refundId));
                })
            .RequireAuthorization()
            .WithName("RequestRefund")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<Guid>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);

        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/refund",
                async (Guid id, IHttpContextService httpContextService, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new GetMyRefundRequestQuery(subscriberId, id), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetMyRefundRequest")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
