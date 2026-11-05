using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.StartTrial;

public record StartTrialRequest(Guid PlanId);

public class StartTrialEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/trial",
                async (StartTrialRequest request, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");

                    var result = await sender.Send(new StartTrialCommand(subscriberId, request.PlanId), ct);
                    return Results.Created($"{EndpointConfig.BaseApiPath}/subscriptions/me",
                        ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .WithIdempotency()
            .WithName("StartTrial")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<StartTrialResult>>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict)
            .HasApiVersion(1.0);
    }
}
