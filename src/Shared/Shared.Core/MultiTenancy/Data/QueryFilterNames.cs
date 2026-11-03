namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Names of the EF Core 10 named query filters. Every filter in the app is NAMED, so code can switch off exactly the
/// filter it means to (<c>IgnoreQueryFilters([name])</c>) without dropping tenant isolation.
/// </summary>
public static class QueryFilterNames
{
    /// <summary>Tenant isolation (<see cref="IMustHaveTenant"/>). Only bypassed by host scope - never by name.</summary>
    public const string Tenant = "Tenant";

    /// <summary>Branch scoping (<see cref="IBranchScoped"/>).</summary>
    public const string Branch = "Branch";

    /// <summary>Soft delete (<c>IEntity.IsDeleted</c>).</summary>
    public const string SoftDelete = "SoftDelete";

    /// <summary>Module-specific visibility rules (e.g. Attachment: only <c>Ready</c> files).</summary>
    public const string Lifecycle = "Lifecycle";

    /// <summary>Every filter that is NOT an isolation boundary - safe to ignore for admin/restore/purge paths.</summary>
    public static IReadOnlyCollection<string> NonIsolation { get; } = [SoftDelete, Lifecycle];
}

public static class TenantQueryableExtensions
{
    /// <summary>
    /// Replacement for the parameterless <c>IgnoreQueryFilters()</c> (which would also drop tenant/branch isolation):
    /// ignores soft-delete and lifecycle filters only. Enforced by an architecture test.
    /// </summary>
    public static IQueryable<TEntity> IgnoreNonIsolationFilters<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class =>
        source.IgnoreQueryFilters(QueryFilterNames.NonIsolation);

    /// <summary>Ignores the branch filter too (tenant isolation stays). For tenant-wide admin views.</summary>
    public static IQueryable<TEntity> IgnoreBranchScope<TEntity>(this IQueryable<TEntity> source)
        where TEntity : class =>
        source.IgnoreQueryFilters([QueryFilterNames.Branch]);
}
