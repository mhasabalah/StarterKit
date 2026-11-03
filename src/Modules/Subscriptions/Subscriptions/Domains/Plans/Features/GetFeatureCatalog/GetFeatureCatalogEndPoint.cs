using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetFeatureCatalog;

public class GetFeatureCatalogEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/feature-definitions",
                async (ISender sender, CancellationToken ct, bool includeInactive = false) =>
                {
                    var result = await sender.Send(new GetFeatureCatalogQuery(includeInactive), ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.PlansManage)
            .WithName("GetFeatureCatalog")
            .WithApiVersionSet(app.NewApiVersionSet("Subscriptions").Build())
            .WithTags("Subscriptions")
            .Produces<ApiResponse<List<FeatureCatalogItemDto>>>(StatusCodes.Status200OK)
            .HasApiVersion(1.0);
    }
}
