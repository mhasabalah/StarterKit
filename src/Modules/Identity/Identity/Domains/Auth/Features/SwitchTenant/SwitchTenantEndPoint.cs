using Identity.Contracts.Auth.Commands;
using Shared.Core.MultiTenancy.Web;

namespace Identity.Domains.Auth.Features.SwitchTenant;

public class SwitchTenantEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/auth/switch-tenant", async (SwitchTenantCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return Results.Ok(new ApiResponse<SwitchTenantResult>(true, StatusCodes.Status200OK, result,
                    "Tenant switched"));
            })
            .WithName("SwitchTenant")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .WithTags("Authentication")
            .Produces<ApiResponse<SwitchTenantResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Switch tenant")
            .WithDescription("Issues a new access/refresh token pair whose active tenant (claim `tid`) is the given " +
                             "tenant. The caller must be an active member. List tenants with GET /api/v1/me/tenants.")
            // The CURRENT token may belong to another tenant (or none): resolution must not reject the request.
            .HostOnly()
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .HasApiVersion(1.0);
    }
}
