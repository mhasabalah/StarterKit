using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.ForceApplyPriceChange;

public record ForceApplyPriceChangeRequest(decimal NewPrice);

public class ForceApplyPriceChangeEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/plans/{id:guid}/force-price-change",
                async (Guid id,
                    ForceApplyPriceChangeRequest request,
                    IHttpContextService httpContextService,
                    ISender sender,
                    CancellationToken ct) =>
                {
                    var adminId = httpContextService.GetUserIdAsGuid()
                        ?? throw new ForbiddenException("Unauthorized");
                    await sender.Send(
                        new ForceApplyPriceChangeCommand(id, adminId, request.NewPrice), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { Status = "Applied" }));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("ForceApplyPriceChange")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .HasApiVersion(1.0);
    }
}
