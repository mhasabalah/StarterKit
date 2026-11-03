using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.DeletePlan;

public class DeletePlanEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/admin/plans/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new DeletePlanCommand(id), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Deleted"));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("DeletePlan")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
