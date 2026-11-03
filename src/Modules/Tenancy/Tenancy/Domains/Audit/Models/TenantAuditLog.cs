namespace Tenancy.Domains.Audit.Models;

/// <summary>
/// Append-only audit trail of administrative actions inside a tenant (membership, roles, branches, settings,
/// lifecycle). Tenant-owned, so each tenant's admins read only their own trail (<c>tenant.audit.read</c>).
/// Updates/deletes are rejected by <c>TenancyDbContext</c> (except the tenant purge, which runs in host scope).
/// </summary>
public class TenantAuditLog : IMustHaveTenant
{
    public const int ActionMaxLength = 100;

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; private set; }

    /// <summary>Platform user acting on behalf of the tenant (impersonation / host admin), when different.</summary>
    public Guid? ImpersonatorUserId { get; private set; }

    public string Action { get; private set; } = default!;
    public string TargetType { get; private set; } = default!;
    public string? TargetId { get; private set; }
    public string? DataJson { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private TenantAuditLog()
    {
    }

    public static TenantAuditLog Create(Guid tenantId, Guid? actorUserId, Guid? impersonatorUserId, string action,
        string targetType, string? targetId, string? dataJson, string? ipAddress, string? correlationId,
        DateTime utcNow) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        ActorUserId = actorUserId,
        ImpersonatorUserId = impersonatorUserId,
        Action = action.Length <= ActionMaxLength ? action : action[..ActionMaxLength],
        TargetType = targetType,
        TargetId = targetId,
        DataJson = dataJson,
        IpAddress = ipAddress,
        CorrelationId = correlationId,
        OccurredAt = utcNow
    };
}

/// <summary>Stable audit action names.</summary>
public static class TenantAuditActions
{
    public const string TenantCreated = "tenant.created";
    public const string TenantUpdated = "tenant.updated";
    public const string TenantSuspended = "tenant.suspended";
    public const string TenantReactivated = "tenant.reactivated";
    public const string TenantDeletionRequested = "tenant.deletion-requested";
    public const string TenantDeletionCancelled = "tenant.deletion-cancelled";
    public const string TenantIsolationChanged = "tenant.isolation-changed";
    public const string TenantDataExported = "tenant.data-exported";
    public const string TenantMoveRequested = "tenant.move-requested";
    public const string TenantMoveCompleted = "tenant.move-completed";
    public const string TenantMoveFailed = "tenant.move-failed";
    public const string ImpersonationStarted = "platform.impersonation-started";
    public const string OwnershipTransferred = "tenant.ownership-transferred";
    public const string DomainAdded = "tenant.domain-added";
    public const string DomainVerified = "tenant.domain-verified";
    public const string DomainRemoved = "tenant.domain-removed";
    public const string MemberInvited = "member.invited";
    public const string InvitationRevoked = "member.invitation-revoked";
    public const string InvitationResent = "member.invitation-resent";
    public const string MemberJoined = "member.joined";
    public const string MemberUpdated = "member.updated";
    public const string MemberSuspended = "member.suspended";
    public const string MemberReactivated = "member.reactivated";
    public const string MemberRemoved = "member.removed";
    public const string MemberLeft = "member.left";
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string BranchCreated = "branch.created";
    public const string BranchUpdated = "branch.updated";
    public const string BranchMoved = "branch.moved";
    public const string BranchDeactivated = "branch.deactivated";
    public const string BranchActivated = "branch.activated";
}
