namespace Shared.Core.Auditing;

/// <summary>Set-based retention of the audit table of a context (the change tracker would reject deletes: append-only).</summary>
public static class AuditTrailRetention
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> entries older than <paramref name="cutoff"/> in the context's
    /// CURRENT tenant scope (query filters apply). Returns the number removed; call repeatedly until it returns less
    /// than the batch size.
    /// </summary>
    public static async Task<int> PurgeAuditEntriesAsync(this DbContext context, DateTime cutoff, int batchSize,
        CancellationToken cancellationToken = default)
    {
        var type = context.FindAuditEntryType();
        if (type is null)
            return 0;

        return type == typeof(TenantAuditEntry)
            ? await PurgeAsync(context.Set<TenantAuditEntry>(), cutoff, batchSize, cancellationToken)
            : await PurgeAsync(context.Set<AuditEntry>(), cutoff, batchSize, cancellationToken);
    }

    private static async Task<int> PurgeAsync<TEntry>(IQueryable<TEntry> entries, DateTime cutoff, int batchSize,
        CancellationToken cancellationToken) where TEntry : AuditEntryBase
    {
        var ids = await entries.Where(e => e.OccurredAt < cutoff)
            .OrderBy(e => e.OccurredAt).Select(e => e.Id).Take(batchSize).ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return 0;

        return await entries.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
