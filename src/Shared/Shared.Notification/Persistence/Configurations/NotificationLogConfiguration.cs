using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Notification.Persistence;

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

        builder.HasIndex(x => x.CorrelationId)
            .HasDatabaseName("idx_notification_log_correlation_id");

        builder.HasIndex(x => new { x.UserId, x.Status, x.CreatedAt })
            .HasDatabaseName("idx_notification_log_user_status_created");
    }
}
