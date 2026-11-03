using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetPlanById;

public class GetPlanByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/plans/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GetPlanByIdQuery(id), ct);
                    return result is null
                        ? ApiResponseExtensions.NotFound("Plan not found")
                        : Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("GetPlanById")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<AdminPlanDto>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
