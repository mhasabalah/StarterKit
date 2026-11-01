namespace Subscriptions.Data.Configurations;

public class AdminActionAuditLogConfiguration : IEntityTypeConfiguration<AdminActionAuditLog>
{
    public void Configure(EntityTypeBuilder<AdminActionAuditLog> builder)
    {
        builder.ToTable("admin_action_audit_logs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.TargetType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Reason)
            .HasMaxLength(1000);

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.HasIndex(x => x.AdminId);
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => x.SubscriberId);
    }
}
