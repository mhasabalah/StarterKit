namespace Shared.Core.MultiTenancy.Data;

/// <summary>
/// Write-side tenant isolation for <see cref="IMustHaveTenant"/> entities:
/// <list type="bullet">
/// <item><c>Added</c>: an empty <c>TenantId</c> is stamped with the current tenant; no tenant → <see cref="TenantNotResolvedException"/>.
/// A preset foreign <c>TenantId</c> is only allowed in host scope.</item>
/// <item><c>Modified</c> / <c>Deleted</c>: the row must belong to the current tenant (stops attach-and-update of
/// foreign ids), and <c>TenantId</c> itself can never change.</item>
/// </list>
/// Host scope (<see cref="TenantScope.BeginHostScope"/>) may write any tenant's rows but must set <c>TenantId</c> explicitly.
/// </summary>
public sealed class TenantEntityInterceptor(ITenantContext tenantContext, ILogger<TenantEntityInterceptor> logger)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        var isHost = tenantContext.IsHostScope;
        var current = tenantContext.TenantId;

        foreach (var entry in context.ChangeTracker.Entries<IMustHaveTenant>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.TenantId == Guid.Empty)
                    {
                        if (isHost)
                        {
                            throw new InvalidOperationException(
                                $"{entry.Metadata.ClrType.Name}: host-scope inserts must set TenantId explicitly.");
                        }

                        entry.Entity.TenantId = current ?? throw new TenantNotResolvedException(
                            $"cannot create {entry.Metadata.ClrType.Name} without a tenant.");
                    }
                    else if (!isHost && entry.Entity.TenantId != current)
                    {
                        throw Violation(entry, "insert");
                    }

                    break;

                case EntityState.Modified:
                case EntityState.Deleted:
                    var tenantProperty = entry.Property(e => e.TenantId);
                    if (tenantProperty.IsModified && tenantProperty.OriginalValue != tenantProperty.CurrentValue)
                    {
                        throw Violation(entry, "re-parent");
                    }

                    if (!isHost && tenantProperty.OriginalValue != current)
                    {
                        throw Violation(entry, entry.State == EntityState.Deleted ? "delete" : "update");
                    }

                    break;
            }
        }
    }

    private CrossTenantAccessException Violation(EntityEntry<IMustHaveTenant> entry, string operation)
    {
        // Security event: someone (or a bug) tried to touch another tenant's row.
        logger.LogWarning(
            "SECURITY: blocked cross-tenant {Operation} of {Entity} (row tenant {RowTenant}, current tenant {CurrentTenant})",
            operation, entry.Metadata.ClrType.Name, entry.Entity.TenantId, tenantContext.TenantId);

        return new CrossTenantAccessException(entry.Metadata.ClrType.Name);
    }
}
