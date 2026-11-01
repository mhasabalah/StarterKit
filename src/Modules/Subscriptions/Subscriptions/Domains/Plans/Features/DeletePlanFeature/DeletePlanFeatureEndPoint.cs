using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.DeletePlanFeature;

public class DeletePlanFeatureEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/admin/plans/{planId:guid}/features/{key}",
                async (Guid planId, string key, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new DeletePlanFeatureCommand(planId, key), ct);
                    return Results.Ok(ApiResponseExtensions.Success("Deleted"));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .WithName("DeletePlanFeature")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces(StatusCodes.Status200OK)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
