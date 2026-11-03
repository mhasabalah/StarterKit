using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.SchedulePriceChange;

public record SchedulePriceChangeRequest(decimal NewPrice, DateTimeOffset EffectiveAt);

public class SchedulePriceChangeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/plans/{id:guid}/schedule-price-change",
                async (Guid id,
                    SchedulePriceChangeRequest request,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(
                        new SchedulePriceChangeCommand(id, adminId, request.NewPrice, request.EffectiveAt), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { Status = "Scheduled" }));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("SchedulePriceChange")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
