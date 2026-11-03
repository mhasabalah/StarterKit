using Identity.Domains.Audit.Models;

namespace Identity.Data.Configurations;

public class AdminActionAuditLogConfiguration : IEntityTypeConfiguration<AdminActionAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminActionAuditLog> builder)
    {
        builder.ToTable("admin_action_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Action).IsRequired().HasMaxLength(100);
        builder.Property(x => x.TargetType).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Reason).HasMaxLength(AdminActionAuditLog.MaxReasonLength);
        builder.Property(x => x.BeforeJson).HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnType("jsonb");
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.UserAgent).HasMaxLength(AdminActionAuditLog.MaxUserAgentLength);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.OccurredAt).IsRequired();

        builder.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_admin_action_audit_logs_occurred_at");
        builder.HasIndex(x => new { x.AdminId, x.OccurredAt }).HasDatabaseName("ix_admin_action_audit_logs_admin_id");
        builder.HasIndex(x => new { x.TargetType, x.TargetId }).HasDatabaseName("ix_admin_action_audit_logs_target");
        builder.HasIndex(x => x.Action).HasDatabaseName("ix_admin_action_audit_logs_action");
    }
}
