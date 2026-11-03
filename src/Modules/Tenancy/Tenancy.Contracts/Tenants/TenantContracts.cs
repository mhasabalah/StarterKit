namespace Tenancy.Contracts.Tenants;

/// <summary>Membership status inside a tenant.</summary>
public enum TenantMembershipStatus
{
    Active = 0,
    Suspended = 1
}

/// <summary>Invitation lifecycle.</summary>
public enum TenantInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3
}

/// <summary>Public tenant profile (what members and the login page may see).</summary>
public sealed record TenantDto(
    Guid Id,
    string Identifier,
    string Name,
    string? LegalName,
    TenantStatus Status,
    TenantIsolation Isolation,
    string? Region,
    string? Culture,
    string? TimeZone,
    string? Currency,
    Guid? LogoAttachmentId,
    string? PrimaryColor,
    Guid OwnerUserId,
    DateTime CreatedAt,
    long Version);

/// <summary>A tenant the current user belongs to (tenant switcher).</summary>
public sealed record UserTenantDto(
    Guid TenantId,
    string Identifier,
    string Name,
    TenantStatus Status,
    bool IsOwner,
    IReadOnlyList<string> Roles,
    bool IsCurrent);

/// <summary>Platform view of a tenant (host admin list).</summary>
public sealed record HostTenantDto(
    Guid Id,
    string Identifier,
    string Name,
    TenantStatus Status,
    TenantIsolation Isolation,
    string? Region,
    Guid OwnerUserId,
    int MemberCount,
    DateTime CreatedAt,
    DateTime? DeletionRequestedAt,
    DateTime? PurgeAfter);

/// <summary>Tenant by id (any module; e.g. to print the tenant name on an invoice).</summary>
public sealed record GetTenantByIdQuery(Guid TenantId) : IQuery<TenantDto?>;

/// <summary>Active tenants of a user (tenant switcher; Identity login).</summary>
public sealed record GetUserTenantsQuery(Guid UserId) : IQuery<IReadOnlyList<UserTenantDto>>;
