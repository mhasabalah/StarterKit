using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.CreateSubscription;

public record CreateSubscriptionRequest(Guid PlanId);

public class CreateSubscriptionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/subscriptions",
                async (CreateSubscriptionRequest request, IHttpContextService httpContextService, ISender sender, CancellationToken ct) =>
                {
                    var subscriberId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");

                    var result = await sender.Send(new CreateSubscriptionCommand(subscriberId, request.PlanId), ct);
                    return Results.Created($"{EndpointConfig.BaseApiPath}/subscriptions/{result.SubscriptionId}",
                        ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization()
            .RequireRateLimiting("create")
            .WithName("CreateSubscription")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<CreateSubscriptionResult>>(StatusCodes.Status201Created)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
