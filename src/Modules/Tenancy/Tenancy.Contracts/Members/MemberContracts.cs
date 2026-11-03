using Tenancy.Contracts.Tenants;

namespace Tenancy.Contracts.Members;

/// <summary>A branch a member works in; <see cref="IncludeDescendants"/> extends the scope to the whole subtree.</summary>
public sealed record BranchAssignment(Guid BranchId, bool IncludeDescendants = true);

public sealed record MemberDto(
    Guid UserId,
    string? DisplayName,
    string? Email,
    TenantMembershipStatus Status,
    bool IsOwner,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> Roles,
    IReadOnlyList<BranchAssignment> Branches,
    DateTime JoinedAt,
    long Version);

public sealed record GetMembersQuery : BaseQuery, IQuery<PaginatedResult<MemberDto>>
{
    public TenantMembershipStatus? Status { get; init; }
    public Guid? RoleId { get; init; }
    public Guid? BranchId { get; init; }
}

/// <summary>The caller's own membership in the current tenant (roles, effective permissions, branch scope).</summary>
public sealed record GetMyMembershipQuery : IQuery<MyMembershipDto>;

public sealed record MyMembershipDto(
    Guid TenantId,
    Guid UserId,
    bool IsOwner,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid>? AllowedBranchIds,
    IReadOnlyList<BranchAssignment> Branches);

public sealed record UpdateMemberCommand(
    Guid UserId,
    List<Guid> RoleIds,
    List<BranchAssignment> Branches,
    long? Version = null) : ICommand<UpdateMemberResult>;

public sealed record UpdateMemberResult(Guid UserId);

public sealed record SetMemberStatusCommand(Guid UserId, bool Active) : ICommand<SetMemberStatusResult>;

public sealed record SetMemberStatusResult(Guid UserId, TenantMembershipStatus Status);

public sealed record RemoveMemberCommand(Guid UserId) : ICommand<RemoveMemberResult>;

public sealed record RemoveMemberResult(Guid UserId);

/// <summary>The caller leaves the current tenant (owners must transfer ownership first).</summary>
public sealed record LeaveTenantCommand : ICommand<LeaveTenantResult>;

public sealed record LeaveTenantResult(Guid TenantId);

// ----- invitations -----

public sealed record InvitationDto(
    Guid Id,
    string Email,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<BranchAssignment> Branches,
    TenantInvitationStatus Status,
    DateTime ExpiresAt,
    Guid InvitedBy,
    DateTime? CreatedAt);

public sealed record GetInvitationsQuery(TenantInvitationStatus? Status = TenantInvitationStatus.Pending)
    : IQuery<IReadOnlyList<InvitationDto>>;

/// <summary>
/// Invites an e-mail and e-mails the invitation link (<c>MultiTenancy:Invitations</c>). Only the token's hash is
/// stored; the raw token is in the e-mail (and in the response only when <c>ReturnTokenInResponse</c> is on).
/// </summary>
public sealed record InviteMemberCommand(
    string Email,
    List<Guid> RoleIds,
    List<BranchAssignment> Branches,
    int? ExpiresInDays = null) : ICommand<InviteMemberResult>;

/// <param name="InvitationId">Invitation.</param>
/// <param name="Token">Raw token - only when <c>MultiTenancy:Invitations:ReturnTokenInResponse</c> is on.</param>
/// <param name="ExpiresAt">Expiry (UTC).</param>
/// <param name="EmailQueued">The invitation e-mail was queued (false: sending disabled or failed - use resend).</param>
public sealed record InviteMemberResult(Guid InvitationId, string? Token, DateTime ExpiresAt, bool EmailQueued);

/// <summary>Re-sends a pending/expired invitation with a NEW token (the old link stops working) and a fresh lifetime.</summary>
public sealed record ResendInvitationCommand(Guid InvitationId, int? ExpiresInDays = null) : ICommand<InviteMemberResult>;

public sealed record RevokeInvitationCommand(Guid InvitationId) : ICommand<RevokeInvitationResult>;

public sealed record RevokeInvitationResult(Guid InvitationId);

/// <summary>The signed-in user (e-mail must match) accepts an invitation and becomes a member.</summary>
public sealed record AcceptInvitationCommand(string Token) : ICommand<AcceptInvitationResult>;

public sealed record AcceptInvitationResult(Guid TenantId, string Identifier, string Name);
