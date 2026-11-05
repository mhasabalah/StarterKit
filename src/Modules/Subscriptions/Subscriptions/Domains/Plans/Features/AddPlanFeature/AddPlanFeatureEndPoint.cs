using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.AddPlanFeature;

public record AddPlanFeatureRequest(string Key, long? Limit = null, QuotaResetPeriod? ResetPeriod = null);

public class AddPlanFeatureEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/plans/{id:guid}/features",
                async (Guid id, AddPlanFeatureRequest request, ISender sender, CancellationToken ct) =>
                {
                    var featureId = await sender.Send(new AddPlanFeatureCommand(id, request.Key, request.Limit, request.ResetPeriod), ct);
                    return Results.Ok(ApiResponseExtensions.Success(new { featureId }));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("AddPlanFeature")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .HasApiVersion(1.0);
    }
}
