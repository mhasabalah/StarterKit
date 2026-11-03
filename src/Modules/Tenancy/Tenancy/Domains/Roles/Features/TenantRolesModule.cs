namespace Tenancy.Domains.Roles.Features;

public record UpdateTenantRoleRequest(string Name, string? Description, List<string> Permissions, long? Version = null);

/// <summary>Tenant roles, the permission catalog and the tenant audit trail.</summary>
public class TenantRolesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Tenancy").Build();
        var group = app.MapGroup(EndpointConfig.BaseApiPath)
            .WithTags("Tenant roles").WithApiVersionSet(versionSet).HasApiVersion(1.0).RequireTenant();

        group.MapGet("/tenant-roles", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetTenantRolesQuery(), ct))))
            .WithName("GetTenantRoles").Produces<ApiResponse<IReadOnlyList<TenantRoleDto>>>()
            .WithSummary("Tenant roles")
            .RequireTenantPermission(TenantPermissions.MembersRead);

        group.MapGet("/tenant-permissions", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetTenantPermissionsQuery(), ct))))
            .WithName("GetTenantPermissions").Produces<ApiResponse<IReadOnlyList<string>>>()
            .WithSummary("Permission catalog")
            .RequireTenantMember();

        group.MapPost("/tenant-roles", async (CreateTenantRoleCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return Results.Created($"api/v1/tenant-roles/{result.Id}", ApiResponseExtensions.Success(result));
            })
            .WithName("CreateTenantRole").Produces<ApiResponse<CreateTenantRoleResult>>(StatusCodes.Status201Created)
            .WithSummary("Create role").WithDescription("You can only grant permissions you hold.")
            .RequireTenantPermission(TenantPermissions.RolesManage);

        group.MapPut("/tenant-roles/{id:guid}", async (Guid id, UpdateTenantRoleRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new UpdateTenantRoleCommand(id, request.Name, request.Description, request.Permissions, request.Version), ct))))
            .WithName("UpdateTenantRole").Produces<ApiResponse<UpdateTenantRoleResult>>()
            .WithSummary("Update role").WithDescription("System roles are read-only.")
            .RequireTenantPermission(TenantPermissions.RolesManage);

        group.MapDelete("/tenant-roles/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteTenantRoleCommand(id), ct))))
            .WithName("DeleteTenantRole").Produces<ApiResponse<DeleteTenantRoleResult>>()
            .WithSummary("Delete role").WithDescription("Only custom roles that no member holds.")
            .RequireTenantPermission(TenantPermissions.RolesManage);

        group.MapGet("/tenant-audit", async ([AsParameters] GetTenantAuditLogQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetTenantAuditLog").Produces<ApiResponse<PaginatedResult<TenantAuditLogDto>>>()
            .WithSummary("Tenant audit trail")
            .RequireTenantPermission(TenantPermissions.AuditRead);
    }
}
