using Shared.Core.MultiTenancy.Authorization;

namespace Catalog.Domains.Products.Features.GetProductHistory;

public class GetProductHistoryEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/products/{id:guid}/history",
                async (Guid id, int? pageNumber, int? pageSize, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new GetProductHistoryQuery(id) { PageNumber = pageNumber ?? 1, PageSize = pageSize ?? 10 },
                        cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .WithName("GetProductHistory")
            .WithTags("Products")
            .WithApiVersionSet(app.NewApiVersionSet("Catalog").Build())
            .Produces<ApiResponse<PaginatedResult<ProductHistoryEntryDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Get Product History")
            .WithDescription(
                "Audit trail of the product, newest first: who changed which properties (old and new values), " +
                "impersonator, correlation id and IP. Paged. Requires the tenant permission `tenant.audit.read`.")
            .RequireTenantPermission(TenantPermissions.AuditRead)
            .HasApiVersion(1.0);
    }
}
