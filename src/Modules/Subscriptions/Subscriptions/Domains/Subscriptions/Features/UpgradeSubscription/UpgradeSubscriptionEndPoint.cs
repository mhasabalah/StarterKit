using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.UpgradeSubscription;

public record UpgradeSubscriptionRequest(Guid NewPlanId);

public class UpgradeSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/upgrade",
                async (Guid id,
                    UpgradeSubscriptionRequest request,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    var result = await sender.Send(
                        new UpgradeSubscriptionCommand(id, subscriberId, request.NewPlanId), ct);
                    return Results.Created(string.Empty, ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .WithName("UpgradeSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<UpgradeSubscriptionResult>>(StatusCodes.Status201Created)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
