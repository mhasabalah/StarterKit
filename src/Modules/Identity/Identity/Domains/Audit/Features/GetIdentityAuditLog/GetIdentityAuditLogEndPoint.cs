using Identity.Contracts.Audit;
using Shared.Core.Authorization;
using Shared.Core.Web;

namespace Identity.Domains.Audit.Features.GetIdentityAuditLog;

public class GetIdentityAuditLogEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/admin/identity/audit",
                async ([AsParameters] GetIdentityAuditLogQuery query, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(query, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequirePermission(Permissions.AuditRead)
            .RequireRateLimiting("search")
            .WithName("GetIdentityAuditLog")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Audit")
            .Produces<ApiResponse<PaginatedResult<IdentityAuditLogDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get the Identity admin audit log")
            .WithDescription(
                "Paged, newest first. Filters: adminId, action, targetType, targetId, from, to. Permission audit.read.")
            .HasApiVersion(1.0);
    }
}
