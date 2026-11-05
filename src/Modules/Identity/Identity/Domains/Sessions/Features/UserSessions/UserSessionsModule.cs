using Identity.Contracts.Sessions;
using Identity.Services.ApiKeys;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web;

namespace Identity.Domains.Sessions.Features.UserSessions;

/// <summary>Support/admin view of a user's sessions (<c>sessions.manage</c>).</summary>
public class UserSessionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/admin/users/{id:guid}/sessions")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("User Management")
            .HasApiVersion(1.0);

        group.MapGet("", async ([FromRoute] Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetUserSessionsQuery(id), ct))))
            .WithName("GetUserSessions")
            .WithSummary("List a user's active sessions")
            .Produces<ApiResponse<List<SessionDto>>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .DisallowApiKey()
            .RequirePermission(Permissions.SessionsManage);

        group.MapDelete("{sessionId:guid}", async (
                [FromRoute] Guid id, [FromRoute] Guid sessionId, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RevokeUserSessionCommand(id, sessionId), ct))))
            .WithName("RevokeUserSession")
            .WithSummary("Sign one of a user's devices out")
            .Produces<ApiResponse<RevokeSessionsResult>>()
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequirePermission(Permissions.SessionsManage);
    }
}
