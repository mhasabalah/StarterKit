using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetRenewalStatus;

public class GetRenewalStatusEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/subscriptions/me/renewal",
                async (IHttpContextService httpContextService, ISubscriberIdProvider subscribers, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(new GetRenewalStatusQuery(subscriberId), ct);
                    return result is null
                        ? ApiResponseExtensions.NotFound("No past due subscription found")
                        : Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .WithName("GetRenewalStatus")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<RenewalStatusDto>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
