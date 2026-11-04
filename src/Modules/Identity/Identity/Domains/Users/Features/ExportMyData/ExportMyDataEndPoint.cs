using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.ExportMyData;

public class ExportMyDataEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("api/identity/profile/export", async (
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContextService.GetUserIdAsGuid();
                if (userId is null)
                    return Results.Unauthorized();

                var result = await sender.Send(new ExportMyDataQuery(userId.Value), cancellationToken);
                return Results.Ok(ApiResponse.Success(result));
            })
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .WithName("ExportMyData")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Profile")
            .WithSummary("Export my personal data")
            .WithDescription(
                "JSON export of the signed-in user's identity data: profile, contact, roles, external logins, " +
                "security flags, active sessions and recent identity audit entries about the user. No secrets.")
            .Produces<ApiResponse<MyDataExportDto>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .HasApiVersion(1.0);
    }
}
