using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.CancelScheduledPriceChange;

public class CancelScheduledPriceChangeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/admin/plans/{id:guid}/schedule-price-change",
                async (Guid id,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(new CancelScheduledPriceChangeCommand(id, adminId), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { Status = "Cancelled" }));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("CancelScheduledPriceChange")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
