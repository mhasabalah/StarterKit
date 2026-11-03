using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Notification.Persistence.Configurations;

public sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_log");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Channel).HasConversion<string>();
        builder.Property(x => x.Status).HasConversion<string>();
        builder.Property(x => x.Culture).HasConversion<string>();
        builder.Property(x => x.MessageJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("idx_notification_log_tenant");

        builder.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("idx_notification_log_correlation_id");

        builder.HasIndex(x => new { x.UserId, x.Status, x.CreatedAt })
            .HasDatabaseName("idx_notification_log_user_status_created");

        // Admin history across all recipients (e.g. ?status=Failed), newest first.
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("idx_notification_log_status_created");

        // Retention purge (created_at < threshold) and the daily failure summary (created_at >= since).
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("idx_notification_log_created");
    }
}
