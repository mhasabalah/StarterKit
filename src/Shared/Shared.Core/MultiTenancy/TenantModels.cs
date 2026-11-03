namespace Shared.Core.MultiTenancy;

/// <summary>Where a tenant's business data lives.</summary>
public enum TenantIsolation
{
    /// <summary>Shared database: rows carry <c>tenant_id</c>; isolated by EF named filters + Postgres RLS.</summary>
    Shared = 0,

    /// <summary>Own database (connection string resolved from <see cref="TenantInfo.ConnectionSecretRef"/>).</summary>
    Dedicated = 1
}

/// <summary>
/// Tenant lifecycle. <see cref="Active"/> serves requests; <see cref="Suspended"/> (read-only mode) and
/// <see cref="Migrating"/> serve reads only.
/// </summary>
public enum TenantStatus
{
    Provisioning = 0,
    Active = 1,
    Suspended = 2,
    PendingDeletion = 3,
    Deleted = 4,
    ProvisioningFailed = 5,

    /// <summary>
    /// Data is being moved between databases (shared ⇄ dedicated). Reads keep working; writes get 503
    /// <c>tenant_migrating</c>; background fan-out skips the tenant.
    /// </summary>
    Migrating = 6
}

/// <summary>What a <see cref="TenantStatus.Suspended"/> tenant may still do.</summary>
public enum SuspendedTenantMode
{
    /// <summary>Every tenant request gets 403 <c>tenant_suspended</c>.</summary>
    Block = 0,

    /// <summary>Safe methods (GET/HEAD/OPTIONS) still work; writes get 403 <c>tenant_suspended</c>.</summary>
    ReadOnly = 1
}

/// <summary>
/// Immutable, cache-friendly snapshot of a tenant - what the request pipeline, DbContexts and jobs need.
/// The Tenancy module owns the full aggregate; this is its read model.
/// </summary>
/// <param name="Id">Stable tenant id (the value stored in every <c>tenant_id</c> column).</param>
/// <param name="Identifier">URL-safe unique slug (subdomain <c>{identifier}.{RootDomain}</c>).</param>
/// <param name="Name">Display name.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="Isolation">Shared or dedicated database.</param>
/// <param name="ConnectionSecretRef">
/// Dedicated tenants only: the NAME of a connection string (looked up in configuration / Docker secrets),
/// never the connection string itself.
/// </param>
/// <param name="Region">Data-residency region label (informational / routing).</param>
/// <param name="Culture">Default culture, e.g. <c>ar-EG</c>.</param>
/// <param name="TimeZone">IANA time zone id.</param>
public sealed record TenantInfo(
    Guid Id,
    string Identifier,
    string Name,
    TenantStatus Status = TenantStatus.Active,
    TenantIsolation Isolation = TenantIsolation.Shared,
    string? ConnectionSecretRef = null,
    string? Region = null,
    string? Culture = null,
    string? TimeZone = null)
{
    public bool IsActive => Status == TenantStatus.Active;
    public bool IsDedicated => Isolation == TenantIsolation.Dedicated;
}

/// <summary>Well-known values of the always-present Default tenant.</summary>
public static class TenantDefaults
{
    /// <summary>
    /// Id of the Default tenant. In single-tenant mode every row belongs to it; switching multi-tenancy on later
    /// keeps all existing data as the Default tenant's data - no migration needed.
    /// </summary>
    public static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public const string DefaultIdentifier = "default";

    public static TenantInfo DefaultTenant { get; } = new(DefaultTenantId, DefaultIdentifier, "Default");
}

/// <summary>
/// The branches a request may see. <see cref="AllowedBranchIds"/> = <c>null</c> means unrestricted
/// (tenant owners/admins, or users with <c>tenant.branches.all</c>); otherwise it is the already-expanded set
/// (assigned branches + descendants where the assignment includes them).
/// </summary>
public sealed record BranchScope(IReadOnlyList<Guid>? AllowedBranchIds, Guid? ActiveBranchId = null)
{
    // Cast: a bare `null` would bind to the record copy constructor and throw.
    public static BranchScope Unrestricted { get; } = new((IReadOnlyList<Guid>?)null);

    public bool IsRestricted => AllowedBranchIds is not null;

    public bool Allows(Guid branchId) => AllowedBranchIds is null || AllowedBranchIds.Contains(branchId);
}

/// <summary>A user's membership in one tenant, as needed per request (cached by the Tenancy module).</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="UserId">Global user id.</param>
/// <param name="IsOwner">Tenant owner (implicitly every tenant permission, unrestricted branches).</param>
/// <param name="Permissions">Effective tenant permissions (union of the membership's tenant roles).</param>
/// <param name="Branches">Effective branch scope.</param>
/// <param name="Stamp">Rotates on every membership change; tokens carry it as <c>tms</c> and go stale when it changes.</param>
public sealed record TenantMembershipInfo(
    Guid TenantId,
    Guid UserId,
    bool IsOwner,
    IReadOnlySet<string> Permissions,
    BranchScope Branches,
    string Stamp)
{
    public bool HasPermission(string permission) => IsOwner || Permissions.Contains(permission);
}
