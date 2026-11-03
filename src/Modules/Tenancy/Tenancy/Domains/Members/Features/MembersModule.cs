using Microsoft.AspNetCore.Mvc;

namespace Tenancy.Domains.Members.Features;

public record UpdateMemberRequest(List<Guid> RoleIds, List<BranchAssignment> Branches, long? Version = null);

/// <summary>Members and invitations of the current tenant.</summary>
public class MembersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Tenancy").Build();

        var members = app.MapGroup(EndpointConfig.BaseApiPath + "/members")
            .WithTags("Members").WithApiVersionSet(versionSet).HasApiVersion(1.0).RequireTenant();

        members.MapGet("/", async ([AsParameters] GetMembersQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetMembers").Produces<ApiResponse<PaginatedResult<MemberDto>>>()
            .WithSummary("Members")
            .RequireTenantPermission(TenantPermissions.MembersRead);

        members.MapGet("/me", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetMyMembershipQuery(), ct))))
            .WithName("GetMyMembership").Produces<ApiResponse<MyMembershipDto>>()
            .WithSummary("My membership").WithDescription("Roles, effective permissions and branch scope of the caller.")
            .RequireTenantMember();

        members.MapPut("/{userId:guid}", async (Guid userId, UpdateMemberRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new UpdateMemberCommand(userId, request.RoleIds, request.Branches, request.Version), ct))))
            .WithName("UpdateMember").Produces<ApiResponse<UpdateMemberResult>>()
            .WithSummary("Update member").WithDescription("Roles and branch assignments. You can only grant what you hold.")
            .RequireTenantPermission(TenantPermissions.MembersManage);

        members.MapPost("/{userId:guid}/suspend", async (Guid userId, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new SetMemberStatusCommand(userId, false), ct))))
            .WithName("SuspendMember").Produces<ApiResponse<SetMemberStatusResult>>()
            .WithSummary("Suspend member")
            .RequireTenantPermission(TenantPermissions.MembersManage);

        members.MapPost("/{userId:guid}/reactivate", async (Guid userId, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new SetMemberStatusCommand(userId, true), ct))))
            .WithName("ReactivateMember").Produces<ApiResponse<SetMemberStatusResult>>()
            .WithSummary("Reactivate member")
            .RequireTenantPermission(TenantPermissions.MembersManage);

        members.MapDelete("/{userId:guid}", async (Guid userId, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RemoveMemberCommand(userId), ct))))
            .WithName("RemoveMember").Produces<ApiResponse<RemoveMemberResult>>()
            .WithSummary("Remove member")
            .RequireTenantPermission(TenantPermissions.MembersManage);

        members.MapPost("/leave", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new LeaveTenantCommand(), ct))))
            .WithName("LeaveTenant").Produces<ApiResponse<LeaveTenantResult>>()
            .WithSummary("Leave tenant").WithDescription("Owners must transfer ownership first.")
            .RequireTenantMember()
            .DisallowImpersonation();

        var invitations = app.MapGroup(EndpointConfig.BaseApiPath + "/tenant-invitations")
            .WithTags("Members").WithApiVersionSet(versionSet).HasApiVersion(1.0).RequireTenant();

        invitations.MapGet("/", async ([FromQuery] TenantInvitationStatus? status, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                    new GetInvitationsQuery(status ?? TenantInvitationStatus.Pending), ct))))
            .WithName("GetTenantInvitations").Produces<ApiResponse<IReadOnlyList<InvitationDto>>>()
            .WithSummary("Invitations")
            .RequireTenantPermission(TenantPermissions.MembersManage);

        invitations.MapPost("/", async (InviteMemberCommand command, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(command, ct))))
            .WithName("InviteMember").Produces<ApiResponse<InviteMemberResult>>()
            .WithSummary("Invite member")
            .WithDescription("E-mails the invitation link (the token is returned only when " +
                             "MultiTenancy:Invitations:ReturnTokenInResponse is on). Counts toward the plan's MaxUsers.")
            .RequireTenantPermission(TenantPermissions.MembersManage)
            .RequireRateLimiting(RateLimitPolicyNames.Create);

        invitations.MapPost("/{id:guid}/resend", async (Guid id, [FromQuery] int? expiresInDays, ISender sender,
                CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new ResendInvitationCommand(id, expiresInDays), ct))))
            .WithName("ResendTenantInvitation").Produces<ApiResponse<InviteMemberResult>>()
            .WithSummary("Re-send invitation").WithDescription("New link (the previous one stops working) and lifetime.")
            .RequireTenantPermission(TenantPermissions.MembersManage)
            .RequireRateLimiting(RateLimitPolicyNames.Create);

        invitations.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new RevokeInvitationCommand(id), ct))))
            .WithName("RevokeTenantInvitation").Produces<ApiResponse<RevokeInvitationResult>>()
            .WithSummary("Revoke invitation")
            .RequireTenantPermission(TenantPermissions.MembersManage);
    }
}
