using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetMySubscription;

public class GetMySubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/me",
                async (IHttpContextService httpContextService, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new GetMySubscriptionQuery(subscriberId), ct);
                    return result is null
                        ? ApiResponseExtensions.NotFound("Subscription not found")
                        : Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetMySubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<SubscriptionDto>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
