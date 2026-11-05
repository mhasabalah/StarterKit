using Identity.Contracts.Scim;
using Shared.Core.Authorization;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web;

namespace Identity.Domains.Scim.Features.ScimDirectories;

public record CreateScimDirectoryRequest(string Name);

public record SetScimDirectoryEnabledRequest(bool Enabled, long? Version = null);

public record MapScimGroupRoleRequest(Guid? TenantRoleId);

/// <summary>
/// SCIM directory administration for the current tenant (same gates as SSO: <c>tenant.sso.manage</c>, plan feature
/// <c>sso</c>, no impersonation or API keys for changes). Tokens are shown once.
/// </summary>
public class ScimDirectoriesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/tenant/scim/directories")
            .WithTags("SCIM 2.0")
            .WithApiVersionSet(app.NewApiVersionSet("Identity").Build())
            .HasApiVersion(1.0);

        group.MapGet("", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetScimDirectoriesQuery(), ct))))
            .WithName("GetScimDirectories").WithSummary("List SCIM directories")
            .Produces<ApiResponse<List<ScimDirectoryDto>>>()
            .Gate(write: false);

        group.MapPost("", async (CreateScimDirectoryRequest request, ISender sender, CancellationToken ct) =>
            {
                var created = await sender.Send(new CreateScimDirectoryCommand(request.Name), ct);
                return Results.Created($"api/v1/tenant/scim/directories/{created.Directory.Id}", ApiResponse.Success(created));
            })
            .WithName("CreateScimDirectory").WithSummary("Create a SCIM directory")
            .WithDescription("Returns the SCIM base URL and the bearer token ONCE. Paste both into the IdP's provisioning app.")
            .Produces<ApiResponse<ScimDirectoryCreatedDto>>(StatusCodes.Status201Created)
            .Gate(write: true);

        group.MapPost("{id:guid}/rotate-token", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new RotateScimDirectoryTokenCommand(id), ct))))
            .WithName("RotateScimDirectoryToken").WithSummary("Rotate the SCIM token")
            .WithDescription("The old token stops working immediately.")
            .Produces<ApiResponse<ScimDirectoryCreatedDto>>()
            .Gate(write: true);

        group.MapPut("{id:guid}/enabled", async (Guid id, SetScimDirectoryEnabledRequest request, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(
                    await sender.Send(new SetScimDirectoryEnabledCommand(id, request.Enabled, request.Version), ct))))
            .WithName("SetScimDirectoryEnabled").WithSummary("Enable or disable a SCIM directory")
            .Produces<ApiResponse<ScimDirectoryDto>>()
            .Gate(write: true);

        group.MapDelete("{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new DeleteScimDirectoryCommand(id), ct))))
            .WithName("DeleteScimDirectory").WithSummary("Delete a SCIM directory")
            .WithDescription("Accounts and memberships it created stay as they are.")
            .Produces<ApiResponse<DeleteScimDirectoryResult>>()
            .Gate(write: true);

        group.MapGet("{id:guid}/groups", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(await sender.Send(new GetScimGroupsQuery(id), ct))))
            .WithName("GetScimDirectoryGroups").WithSummary("Groups pushed by the directory")
            .Produces<ApiResponse<List<ScimGroupDto>>>()
            .Gate(write: false);

        group.MapPut("{id:guid}/groups/{groupId:guid}/role", async (Guid id, Guid groupId, MapScimGroupRoleRequest request,
                ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponse.Success(
                    await sender.Send(new MapScimGroupRoleCommand(id, groupId, request.TenantRoleId), ct))))
            .WithName("MapScimGroupRole").WithSummary("Map a directory group to a tenant role")
            .WithDescription("Once any group is mapped, the directory owns its users' roles (roles = their groups' roles).")
            .Produces<ApiResponse<ScimGroupDto>>()
            .Gate(write: true);
    }
}

internal static class ScimEndpointConventions
{
    public static RouteHandlerBuilder Gate(this RouteHandlerBuilder builder, bool write)
    {
        builder.RequireTenantPermission(TenantPermissions.SsoManage)
            .RequireFeature(EntitlementKeys.Sso)
            .DisallowApiKey();
        return write ? builder.DisallowImpersonation() : builder;
    }
}
