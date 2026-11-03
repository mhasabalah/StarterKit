using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.CancelScheduledDowngrade;

public class CancelScheduledDowngradeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/scheduled-downgrade",
                async (Guid id,
                    IHttpContextService httpContextService, ISubscriberIdProvider subscribers,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = subscribers.GetCurrentSubscriberId()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new CancelScheduledDowngradeCommand(id, subscriberId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { Status = "Cancelled" }));
                })
            .RequireAuthorization()
            .WithName("CancelScheduledDowngrade")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
