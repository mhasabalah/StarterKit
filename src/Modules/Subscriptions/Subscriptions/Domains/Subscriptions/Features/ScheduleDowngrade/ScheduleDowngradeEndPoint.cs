using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.ScheduleDowngrade;

public record ScheduleDowngradeRequest(Guid NewPlanId);

public class ScheduleDowngradeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/schedule-downgrade",
                async (Guid id,
                    ScheduleDowngradeRequest request,
                    IHttpContextService httpContextService, ISubscriberIdProvider subscribers,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(
                        new ScheduleDowngradeCommand(id, subscriberId, request.NewPlanId), ct);
                    return Results.Created(string.Empty, ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .WithName("ScheduleDowngrade")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<ScheduleDowngradeResult>>(StatusCodes.Status201Created)
            .HasApiVersion(1.0);
    }
}
