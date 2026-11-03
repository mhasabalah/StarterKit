namespace Tenancy.Domains.Members.Models;

/// <summary>
/// A global user's membership in one tenant (the "join" of users × tenants). Tenant-owned; unique per
/// (tenant, user). Carries the tenant roles, the branch assignments and the ownership flag.
/// <para>
/// <see cref="Stamp"/> rotates on EVERY change; access tokens carry it (<c>tms</c>), so a token issued before a
/// change is rejected as stale (401) and the client refreshes it - the refresh re-reads the membership.
/// </para>
/// </summary>
public class TenantMembership : Aggregate<Guid>, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; private set; }
    public TenantMembershipStatus Status { get; private set; }
    public bool IsOwner { get; private set; }

    /// <summary>Tenant role ids (Postgres <c>uuid[]</c>).</summary>
    public List<Guid> RoleIds { get; private set; } = [];

    public string Stamp { get; private set; } = default!;
    public DateTime JoinedAt { get; private set; }
    public Guid? InvitedBy { get; private set; }

    /// <summary>Branch assignments (<c>jsonb</c>). Empty = no branch restriction is derived from assignments.</summary>
    public List<BranchAssignment> Branches { get; private set; } = [];

    private TenantMembership()
    {
    }

    public static TenantMembership Create(Guid tenantId, Guid userId, IEnumerable<Guid> roleIds,
        IEnumerable<BranchAssignment> branches, DateTime utcNow, bool isOwner = false, Guid? invitedBy = null)
    {
        if (userId == Guid.Empty) throw new InvalidMembershipException("User id is required.");

        var membership = new TenantMembership
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Status = TenantMembershipStatus.Active,
            IsOwner = isOwner,
            JoinedAt = utcNow,
            InvitedBy = invitedBy
        };

        membership.SetRolesCore(roleIds);
        membership.SetBranchesCore(branches);
        membership.Rotate();
        membership.AddDomainEvent(new TenantMemberJoinedEvent(tenantId, userId));
        return membership;
    }

    public void SetRoles(IEnumerable<Guid> roleIds)
    {
        SetRolesCore(roleIds);
        Changed();
    }

    public void SetBranches(IEnumerable<BranchAssignment> branches)
    {
        SetBranchesCore(branches);
        Changed();
    }

    public void Suspend()
    {
        if (IsOwner) throw new InvalidMembershipException("The owner cannot be suspended; transfer ownership first.");
        Status = TenantMembershipStatus.Suspended;
        Changed();
    }

    public void Activate()
    {
        Status = TenantMembershipStatus.Active;
        Changed();
    }

    public void GrantOwnership()
    {
        IsOwner = true;
        Status = TenantMembershipStatus.Active;
        Changed();
    }

    public void RevokeOwnership()
    {
        IsOwner = false;
        Changed();
    }

    /// <summary>Marks the membership as removed (the row is deleted by the caller) and notifies cache owners.</summary>
    public void Remove()
    {
        if (IsOwner) throw new InvalidMembershipException("The owner cannot leave or be removed; transfer ownership first.");
        AddDomainEvent(new TenantMembershipChangedEvent(TenantId, UserId, Removed: true));
    }

    private void SetRolesCore(IEnumerable<Guid> roleIds) =>
        RoleIds = roleIds.Where(id => id != Guid.Empty).Distinct().ToList();

    private void SetBranchesCore(IEnumerable<BranchAssignment> branches)
    {
        Branches = branches
            .Where(b => b.BranchId != Guid.Empty)
            .GroupBy(b => b.BranchId)
            .Select(g => new BranchAssignment(g.Key, g.Any(b => b.IncludeDescendants)))
            .ToList();
    }

    private void Changed()
    {
        Rotate();
        AddDomainEvent(new TenantMembershipChangedEvent(TenantId, UserId, Removed: false));
    }

    private void Rotate() => Stamp = Guid.NewGuid().ToString("N");
}
