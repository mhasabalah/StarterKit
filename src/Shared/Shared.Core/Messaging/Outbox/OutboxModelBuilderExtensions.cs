using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Core.MultiTenancy;

namespace Shared.Core.Messaging.Outbox;

/// <summary>
/// Maps the transactional outbox table (<c>&lt;schema&gt;.outbox_messages</c>) into a module DbContext. Call it in
/// <c>OnModelCreating</c> of every context whose aggregates raise integration events, then add a migration (and, for
/// tenant-data contexts, <c>migrationBuilder.EnableTenantRowLevelSecurity("&lt;schema&gt;", "outbox_messages")</c>) and
/// register the context with <c>services.AddOutbox&lt;TContext&gt;()</c>.
/// </summary>
public static class OutboxModelBuilderExtensions
{
    public const string TableName = "outbox_messages";

    /// <summary>Host context (Subscriptions, Tenancy, Identity...): <see cref="OutboxMessage"/>.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder builder)
    {
        builder.Entity<OutboxMessage>(entity => Configure(entity));
        return builder;
    }

    /// <summary>
    /// Tenant-data context (<see cref="ITenantDataContext"/>: Catalog, Attachment...): <see cref="TenantOutboxMessage"/>.
    /// Call it BEFORE <c>ApplyTenantFilters(builder)</c> so the row gets the named tenant filter like every other entity.
    /// </summary>
    public static ModelBuilder AddTenantOutbox(this ModelBuilder builder)
    {
        builder.Entity<TenantOutboxMessage>(entity =>
        {
            Configure(entity);
            entity.HasIndex(m => m.TenantId);
        });
        return builder;
    }

    private static void Configure<TMessage>(EntityTypeBuilder<TMessage> entity) where TMessage : OutboxMessageBase
    {
        entity.ToTable(TableName);
        entity.HasKey(m => m.Id);
        entity.Property(m => m.Id).ValueGeneratedNever();
        entity.Property(m => m.MessageType).HasMaxLength(OutboxMessageBase.MessageTypeMaxLength).IsRequired();
        entity.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        entity.Property(m => m.TraceParent).HasMaxLength(128);
        entity.Property(m => m.LastError).HasMaxLength(OutboxMessageBase.LastErrorMaxLength);

        // The relay's hot path: pending rows by due time. Partial, so delivered history never bloats it.
        entity.HasIndex(m => m.NextAttemptAt)
            .HasDatabaseName($"ix_{TableName}_pending")
            .HasFilter("processed_at IS NULL AND dead_lettered_at IS NULL");

        // Retention cleanup.
        entity.HasIndex(m => m.ProcessedAt)
            .HasDatabaseName($"ix_{TableName}_processed_at")
            .HasFilter("processed_at IS NOT NULL");
    }

    /// <summary>The outbox row type mapped in <paramref name="context"/>, or <c>null</c> when it has no outbox.</summary>
    public static Type? FindOutboxMessageType(this DbContext context)
    {
        if (context.Model.FindEntityType(typeof(TenantOutboxMessage)) is not null)
            return typeof(TenantOutboxMessage);
        if (context.Model.FindEntityType(typeof(OutboxMessage)) is not null)
            return typeof(OutboxMessage);
        return null;
    }
}
