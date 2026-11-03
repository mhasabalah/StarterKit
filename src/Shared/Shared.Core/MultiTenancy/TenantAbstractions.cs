using System.Net;
using Shared.Core.Exceptions;

namespace Shared.Core.MultiTenancy;

/// <summary>Entity owned by exactly one tenant. <c>TenantId</c> is stamped on insert and immutable afterwards.</summary>
public interface IMustHaveTenant
{
    Guid TenantId { get; set; }
}

/// <summary>
/// Entity that may belong to a branch. <c>BranchId = null</c> means tenant-wide (visible to every branch scope).
/// Branch-restricted users only see rows of their allowed branches (named query filter <c>Branch</c>).
/// </summary>
public interface IBranchScoped
{
    Guid? BranchId { get; set; }
}

/// <summary>
/// Marks a DbContext as DATA PLANE: its database is the shared database or the tenant's dedicated database
/// (resolved per scope), every entity must be <see cref="IMustHaveTenant"/> (or <see cref="HostEntityAttribute"/>),
/// and the session runs with the Postgres RLS variables set.
/// <para>Contexts without this marker are CONTROL PLANE (host database: identity, tenancy, billing, logs).</para>
/// </summary>
public interface ITenantDataContext;

/// <summary>Opt-out for a genuinely global table inside an <see cref="ITenantDataContext"/> (enforced by architecture tests).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HostEntityAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

/// <summary>Tenant lookup (implemented and cached by the Tenancy module; <see cref="DefaultTenantStore"/> otherwise).</summary>
public interface ITenantStore
{
    Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>Verified custom domain → tenant.</summary>
    Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default);

    /// <summary>Tenants that background fan-out must visit (Active and Suspended - suspension stops users, not upkeep).</summary>
    Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Membership lookup (implemented and cached by the Tenancy module; returns <c>null</c> otherwise).</summary>
public interface ITenantMembershipResolver
{
    /// <summary>The ACTIVE membership of <paramref name="userId"/> in an active/serviceable tenant, else <c>null</c>.</summary>
    Task<TenantMembershipInfo?> GetMembershipAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Tenants where <paramref name="userId"/> has an active membership (login tenant selection).</summary>
    Task<IReadOnlyList<Guid>> GetTenantIdsOfUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Drops cached memberships of a user in a tenant (both null = everything) after a change.</summary>
    Task InvalidateAsync(Guid? tenantId = null, Guid? userId = null, CancellationToken cancellationToken = default);
}

/// <summary>Picks the database of a tenant-data DbContext for the current scope.</summary>
public interface ITenantConnectionStringResolver
{
    /// <param name="tenant">Current tenant (<c>null</c> in host scope / unresolved).</param>
    /// <param name="sharedConnectionString">The shared (host) database connection string.</param>
    string Resolve(TenantInfo? tenant, string sharedConnectionString);
}

/// <summary>Single-tenant store: only the Default tenant exists.</summary>
public sealed class DefaultTenantStore : ITenantStore
{
    private static readonly IReadOnlyList<TenantInfo> All = [TenantDefaults.DefaultTenant];

    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenantId == TenantDefaults.DefaultTenantId ? TenantDefaults.DefaultTenant : null);

    public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Equals(identifier, TenantDefaults.DefaultIdentifier, StringComparison.OrdinalIgnoreCase)
            ? TenantDefaults.DefaultTenant
            : null);

    public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default) =>
        Task.FromResult<TenantInfo?>(null);

    public Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(All);
}

/// <summary>No memberships (single-tenant without the Tenancy module): permissions fall back to platform claims.</summary>
public sealed class NullTenantMembershipResolver : ITenantMembershipResolver
{
    public Task<TenantMembershipInfo?> GetMembershipAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken = default) => Task.FromResult<TenantMembershipInfo?>(null);

    public Task<IReadOnlyList<Guid>> GetTenantIdsOfUserAsync(Guid userId,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task InvalidateAsync(Guid? tenantId = null, Guid? userId = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

// ----------------------------------------------------------------------------------------------------------------
// Exceptions (all map to client-safe 4xx through ExceptionStatusMapper via CustomException.StatusCode)
// ----------------------------------------------------------------------------------------------------------------

/// <summary>Stable machine-readable error identifiers (returned in the message; clients may switch on them).</summary>
public static class TenantErrors
{
    public const string TenantRequired = "tenant_required";
    public const string TenantNotFound = "tenant_not_found";
    public const string TenantMismatch = "tenant_mismatch";
    public const string TenantSuspended = "tenant_suspended";
    public const string TenantUnavailable = "tenant_unavailable";
    public const string NotAMember = "tenant_access_denied";
    public const string StaleMembership = "tenant_membership_changed";
    public const string BranchNotAllowed = "branch_not_allowed";
    public const string MultiTenancyDisabled = "multi_tenancy_disabled";
    public const string TenantMigrating = "tenant_migrating";
    public const string ImpersonationRestricted = "impersonation_restricted";
}

/// <summary>
/// The tenant's data is being moved between databases (503 + <c>Retry-After</c>): reads keep working, writes are
/// refused until the move completes.
/// </summary>
public sealed class TenantMigratingException()
    : CustomException($"{TenantErrors.TenantMigrating}: this tenant's data is being moved; it is read-only for a few minutes.",
        HttpStatusCode.ServiceUnavailable);

/// <summary>A tenant is required but none was resolved (400).</summary>
public sealed class TenantNotResolvedException(string? detail = null)
    : CustomException($"{TenantErrors.TenantRequired}: {detail ?? "this operation requires a tenant (sign in to a tenant, or send the tenant header/host)."}",
        HttpStatusCode.BadRequest);

/// <summary>The caller may not act in this tenant (403).</summary>
public sealed class TenantAccessDeniedException(string error, string message)
    : CustomException($"{error}: {message}", HttpStatusCode.Forbidden)
{
    public string Error { get; } = error;
}

/// <summary>The token's membership stamp is stale (401) - the client must refresh its token.</summary>
public sealed class StaleTenantMembershipException()
    : CustomException($"{TenantErrors.StaleMembership}: your access to this tenant changed; refresh your session.",
        HttpStatusCode.Unauthorized);

/// <summary>
/// A write touched a row of another tenant. Surfaces as 404 (never confirm that foreign ids exist) and is logged as
/// a security event by the interceptor.
/// </summary>
public sealed class CrossTenantAccessException(string entity)
    : CustomException("The requested resource was not found.", HttpStatusCode.NotFound)
{
    public string Entity { get; } = entity;
}
