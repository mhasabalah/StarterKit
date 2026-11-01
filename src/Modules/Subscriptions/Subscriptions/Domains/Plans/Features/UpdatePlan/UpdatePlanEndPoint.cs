using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.UpdatePlan;

public class UpdatePlanEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut(EndpointConfig.BaseApiPath + "/admin/plans/{id:guid}",
                async (Guid id, UpdatePlanCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command with { PlanId = id }, ct);
                    return Results.Ok(ApiResponseExtensions.Success("Updated"));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("UpdatePlan")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
