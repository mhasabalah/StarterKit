using Shared.Core.MultiTenancy;

namespace Shared.Core.Auditing;

/// <summary>
/// Opt-in marker: changes (insert / update / delete) of an entity implementing it are recorded as
/// <see cref="AuditEntryBase"/> rows by <see cref="AuditTrailInterceptor"/>, in the SAME SaveChanges (so the same
/// database transaction) as the change itself. A DbContext that maps <see cref="IAuditable"/> entities must map the
/// audit table too (<c>builder.AddAuditTrail()</c> / <c>builder.AddTenantAuditTrail()</c>); an architecture test and the
/// interceptor both enforce it. See wiki/Decisions/0011-audit-trail.md.
/// </summary>
public interface IAuditable;

/// <summary>
/// Keeps a property out of the audit trail (secrets, large blobs, noise). Properties whose name contains
/// <c>Password</c>, <c>Secret</c>, <c>Token</c> or <c>Hash</c> are skipped by default even without it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class AuditIgnoreAttribute : Attribute;

public enum AuditAction
{
    Added,
    Modified,
    Deleted
}

/// <summary>
/// One recorded change of an <see cref="IAuditable"/> entity. Append-only (the interceptor rejects updates/deletes
/// outside host scope; the retention job removes old rows with a set-based delete). Two concrete row types exist for
/// the same reason as the outbox: tenant-data contexts require every entity to be tenant-owned, host contexts may
/// record changes made outside any tenant.
/// </summary>
public abstract class AuditEntryBase
{
    public const string TableName = "audit_entries";
    public const int EntityTypeMaxLength = 128;
    public const int EntityIdMaxLength = 128;
    public const int IpAddressMaxLength = 64;
    public const int CorrelationIdMaxLength = 128;

    public Guid Id { get; set; }

    /// <summary>CLR name of the entity (e.g. <c>Product</c>).</summary>
    public string EntityType { get; set; } = default!;

    /// <summary>Primary key of the entity (composite keys joined by <c>,</c>).</summary>
    public string EntityId { get; set; } = default!;

    public AuditAction Action { get; set; }

    /// <summary>
    /// JSON object <c>{ "Property": { "old": ..., "new": ... } }</c> (<c>old</c> absent on insert, <c>new</c> absent on
    /// delete). Owned values are flattened as <c>Name.En</c>. When capped: <c>{ "_truncated": true, "properties": [...] }</c>.
    /// </summary>
    public string ChangesJson { get; set; } = "{}";

    public Guid? ActorUserId { get; set; }

    /// <summary>Platform user acting on behalf of <see cref="ActorUserId"/> (the <c>act</c> claim), if any.</summary>
    public Guid? ImpersonatorUserId { get; set; }

    public string? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>Audit row of a HOST context: the tenant is optional.</summary>
public sealed class AuditEntry : AuditEntryBase
{
    public Guid? TenantId { get; set; }
}

/// <summary>Audit row of a tenant-data context: tenant-owned (filter, RLS, moved / exported / purged with the tenant).</summary>
public sealed class TenantAuditEntry : AuditEntryBase, IMustHaveTenant
{
    public Guid TenantId { get; set; }
}
