using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Notification.Persistence;

public sealed class UserNotificationPreferenceConfiguration : IEntityTypeConfiguration<UserNotificationPreference>
{
    public void Configure(EntityTypeBuilder<UserNotificationPreference> builder)
    {
        builder.ToTable("user_notification_preference");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChannelOrder)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<NotificationChannel[]>(v, (JsonSerializerOptions?)null)
                    ?? Array.Empty<NotificationChannel>());

        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("idx_notification_pref_user_id");
    }
}
