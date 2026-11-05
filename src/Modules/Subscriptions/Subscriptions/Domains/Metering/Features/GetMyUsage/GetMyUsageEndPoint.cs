using Subscriptions.Contracts.Subscribers;

namespace Subscriptions.Domains.Metering.Features.GetMyUsage;

public class GetMyUsageEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/me/usage",
                async (ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new GetMyUsageQuery(subscriberId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetMyUsage")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<List<UsageDto>>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
