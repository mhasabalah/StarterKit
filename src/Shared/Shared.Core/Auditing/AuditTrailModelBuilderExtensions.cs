using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Core.Auditing;

/// <summary>
/// Maps the audit table (<c>&lt;schema&gt;.audit_entries</c>) into a module DbContext, like the outbox. Call it in
/// <c>OnModelCreating</c> of every context that maps <see cref="IAuditable"/> entities, add a migration (and, for
/// tenant-data contexts, <c>migrationBuilder.EnableTenantRowLevelSecurity("&lt;schema&gt;", "audit_entries")</c>).
/// </summary>
public static class AuditTrailModelBuilderExtensions
{
    /// <summary>Host context: <see cref="AuditEntry"/>.</summary>
    public static ModelBuilder AddAuditTrail(this ModelBuilder builder)
    {
        builder.Entity<AuditEntry>(entity =>
        {
            Configure(entity);
            entity.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAt })
                .HasDatabaseName($"ix_{AuditEntryBase.TableName}_entity");
        });
        return builder;
    }

    /// <summary>
    /// Tenant-data context: <see cref="TenantAuditEntry"/>. Call it BEFORE <c>ApplyTenantFilters(builder)</c> so the
    /// row gets the named tenant filter like every other entity.
    /// </summary>
    public static ModelBuilder AddTenantAuditTrail(this ModelBuilder builder)
    {
        builder.Entity<TenantAuditEntry>(entity =>
        {
            Configure(entity);
            entity.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId, a.OccurredAt })
                .HasDatabaseName($"ix_{AuditEntryBase.TableName}_entity");
        });
        return builder;
    }

    private static void Configure<TEntry>(EntityTypeBuilder<TEntry> entity) where TEntry : AuditEntryBase
    {
        entity.ToTable(AuditEntryBase.TableName);
        entity.HasKey(a => a.Id);
        entity.Property(a => a.Id).ValueGeneratedNever();
        entity.Property(a => a.EntityType).HasMaxLength(AuditEntryBase.EntityTypeMaxLength).IsRequired();
        entity.Property(a => a.EntityId).HasMaxLength(AuditEntryBase.EntityIdMaxLength).IsRequired();
        entity.Property(a => a.Action).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(a => a.ChangesJson).HasColumnType("jsonb").IsRequired();
        entity.Property(a => a.IpAddress).HasMaxLength(AuditEntryBase.IpAddressMaxLength);
        entity.Property(a => a.CorrelationId).HasMaxLength(AuditEntryBase.CorrelationIdMaxLength);

        // Retention cleanup.
        entity.HasIndex(a => a.OccurredAt).HasDatabaseName($"ix_{AuditEntryBase.TableName}_occurred_at");
    }

    /// <summary>The audit row type mapped in <paramref name="context"/>, or <c>null</c> when it has no audit table.</summary>
    public static Type? FindAuditEntryType(this DbContext context) => FindAuditEntryType(context.Model);

    public static Type? FindAuditEntryType(this IModel model)
    {
        if (model.FindEntityType(typeof(TenantAuditEntry)) is not null) return typeof(TenantAuditEntry);
        if (model.FindEntityType(typeof(AuditEntry)) is not null) return typeof(AuditEntry);
        return null;
    }
}
