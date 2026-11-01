using Subscriptions.Contracts.Subscriptions.Commands.Plans;

namespace Subscriptions.Domains.Plans.Features.CreatePlan;

public class CreatePlanEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/admin/plans",
                async (CreatePlanCommand command, ISender sender, CancellationToken ct) =>
                {
                    var planId = await sender.Send(command, ct);
                    return Results.Created($"{EndpointConfig.BaseApiPath}/admin/plans/{planId}",
                        ApiResponseExtensions.Success(new { planId }));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("create")
            .WithName("CreatePlan")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces(StatusCodes.Status201Created)
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
