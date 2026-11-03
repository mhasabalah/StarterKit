using Microsoft.AspNetCore.Mvc;

namespace Tenancy.Domains.Branches.Features;

public record UpdateBranchRequest(string Name, string? Address, string? Phone, string? TimeZone, long? Version = null);

public record MoveBranchRequest(Guid? NewParentId, long? Version = null);

/// <summary>Branch tree of the current tenant.</summary>
public class BranchesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/branches")
            .WithTags("Branches")
            .WithApiVersionSet(app.NewApiVersionSet("Tenancy").Build())
            .HasApiVersion(1.0)
            .RequireTenant();

        group.MapGet("/", async ([FromQuery] bool? includeInactive, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetBranchesQuery(includeInactive ?? false), ct))))
            .WithName("GetBranches").Produces<ApiResponse<IReadOnlyList<BranchTreeNodeDto>>>()
            .WithSummary("Branch tree")
            .WithDescription("Whole tree for unrestricted members; otherwise the caller's branches and their subtrees.")
            .RequireTenantPermission(TenantPermissions.BranchesRead);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetBranchByIdQuery(id), ct))))
            .WithName("GetBranchById").Produces<ApiResponse<BranchDto>>()
            .WithSummary("Branch")
            .RequireTenantPermission(TenantPermissions.BranchesRead);

        group.MapPost("/", async (CreateBranchCommand command, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(command, ct);
                return Results.Created($"api/v1/branches/{result.Id}", ApiResponseExtensions.Success(result));
            })
            .WithName("CreateBranch").Produces<ApiResponse<CreateBranchResult>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Create branch")
            .RequireTenantPermission(TenantPermissions.BranchesManage);

        group.MapPut("/{id:guid}", async (Guid id, UpdateBranchRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new UpdateBranchCommand(id, request.Name, request.Address, request.Phone, request.TimeZone, request.Version), ct))))
            .WithName("UpdateBranch").Produces<ApiResponse<UpdateBranchResult>>()
            .WithSummary("Update branch")
            .RequireTenantPermission(TenantPermissions.BranchesManage);

        group.MapPost("/{id:guid}/move", async (Guid id, MoveBranchRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new MoveBranchCommand(id, request.NewParentId, request.Version), ct))))
            .WithName("MoveBranch").Produces<ApiResponse<MoveBranchResult>>()
            .WithSummary("Move branch").WithDescription("Re-parents the branch and its whole subtree.")
            .RequireTenantPermission(TenantPermissions.BranchesManage);

        group.MapPost("/{id:guid}/activate", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new SetBranchActiveCommand(id, true), ct))))
            .WithName("ActivateBranch").Produces<ApiResponse<SetBranchActiveResult>>()
            .WithSummary("Activate branch")
            .RequireTenantPermission(TenantPermissions.BranchesManage);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new SetBranchActiveCommand(id, false), ct))))
            .WithName("DeactivateBranch").Produces<ApiResponse<SetBranchActiveResult>>()
            .WithSummary("Deactivate branch").WithDescription("Branches are never deleted; children must be inactive.")
            .RequireTenantPermission(TenantPermissions.BranchesManage);
    }
}
