using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.CreateSubscription;

public record CreateSubscriptionRequest(Guid PlanId);

public class CreateSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions",
                async (CreateSubscriptionRequest request, IHttpContextService httpContextService, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");

                    var result = await sender.Send(new CreateSubscriptionCommand(subscriberId, request.PlanId), ct);
                    return Results.Created($"{EndpointConfig.BaseApiPath}/subscriptions/{result.SubscriptionId}",
                        ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .WithIdempotency()
            .WithName("CreateSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<CreateSubscriptionResult>>(StatusCodes.Status201Created)
            .HasApiVersion(1.0);
    }
}
