using Identity.Contracts.Impersonation;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Web;

namespace Identity.Domains.Admins.Features.StartImpersonation;

public class StartImpersonationEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/identity/impersonations", async (StartImpersonationCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(command, ct))))
            .WithName("StartImpersonation")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Admin Management")
            .Produces<ApiResponse<StartImpersonationResult>>()
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .WithSummary("Impersonate a tenant member (support)")
            .WithDescription("Short-lived access token acting as the user in the tenant (no refresh token). " +
                             "Cannot change credentials/account settings, switch tenant or perform owner actions; " +
                             "audited in the admin audit and the tenant's audit trail. Staff accounts cannot be impersonated.")
            .HostOnly()
            .RequirePermission(Permissions.TenantsImpersonate)
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}
