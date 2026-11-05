using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Contracts.InApp;

namespace Notification.Persistence.Configurations;

public sealed class InboxNotificationConfiguration : IEntityTypeConfiguration<InboxNotification>
{
    public void Configure(EntityTypeBuilder<InboxNotification> builder)
    {
        builder.ToTable("inbox_notification");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Category).HasMaxLength(InAppContent.CategoryMaxLength).IsRequired();
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Title).HasMaxLength(InAppContent.TitleMaxLength).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(InAppContent.BodyMaxLength).IsRequired();
        builder.Property(x => x.ActionUrl).HasMaxLength(InAppContent.ActionUrlMaxLength);
        builder.Property(x => x.Data)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null),
                new ValueComparer<Dictionary<string, string>?>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(StringComparison.Ordinal),
                    v => v == null ? null : new Dictionary<string, string>(v)));

        // Idempotency of InAppDeliveryJob: a retried job cannot create a second entry for the same send.
        builder.HasIndex(x => new { x.UserId, x.CorrelationId })
            .IsUnique()
            .HasDatabaseName("ux_inbox_notification_user_correlation");

        // The inbox page: a user's entries, newest first.
        builder.HasIndex(x => new { x.UserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_inbox_notification_user_created");

        // The unread badge (user_id = @u AND read_at IS NULL) stays a small partial-index scan.
        builder.HasIndex(x => new { x.UserId, x.TenantId })
            .HasFilter("read_at IS NULL")
            .HasDatabaseName("idx_inbox_notification_user_unread");

        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("idx_inbox_notification_expires");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_inbox_notification_tenant");
    }
}
