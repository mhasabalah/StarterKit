using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.CancelScheduledDowngrade;

public class CancelScheduledDowngradeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/subscriptions/{id:guid}/scheduled-downgrade",
                async (Guid id,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var subscriberId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new CancelScheduledDowngradeCommand(id, subscriberId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { Status = "Cancelled" }));
                })
            .RequireAuthorization()
            .WithName("CancelScheduledDowngrade")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
