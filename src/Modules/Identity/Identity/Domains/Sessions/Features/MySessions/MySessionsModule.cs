using Identity.Contracts.Sessions;
using Shared.Core.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web;

namespace Identity.Domains.Sessions.Features.MySessions;

/// <summary>Self-service session management ("where am I signed in").</summary>
public class MySessionsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/me/sessions")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Sessions")
            .HasApiVersion(1.0);

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetMySessionsQuery(), ct))))
            .WithName("GetMySessions")
            .WithSummary("List my active sessions")
            .WithDescription("One entry per signed-in device; the session making this request has isCurrent=true.")
            .Produces<ApiResponse<List<SessionDto>>>()
            .DisallowApiKey()
            .RequireAuthorization();

        group.MapDelete("{id:guid}", async ([FromRoute] Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RevokeMySessionCommand(id), ct))))
            .WithName("RevokeMySession")
            .WithSummary("Sign one of my devices out")
            .WithDescription("Revokes the session's refresh tokens and immediately invalidates its access token.")
            .Produces<ApiResponse<RevokeSessionsResult>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireAuthorization();

        group.MapPost("revoke-others", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RevokeOtherSessionsCommand(), ct))))
            .WithName("RevokeOtherSessions")
            .WithSummary("Sign out of all other devices")
            .WithDescription("Keeps the current session; every other session is revoked immediately.")
            .Produces<ApiResponse<RevokeSessionsResult>>()
            .DisallowApiKey()
            .DisallowImpersonation()
            .RequireAuthorization();
    }
}
