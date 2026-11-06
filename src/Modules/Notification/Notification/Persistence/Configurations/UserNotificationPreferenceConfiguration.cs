using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Notification.Persistence.Configurations;

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

        // Mutes are read as one document on every delivery; jsonb keeps the group list open-ended without a join table.
        builder.Property(x => x.CategoryMutes)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, ChannelMuteSet>>(v, (JsonSerializerOptions?)null)
                     ?? new Dictionary<string, ChannelMuteSet>(StringComparer.Ordinal),
                new ValueComparer<Dictionary<string, ChannelMuteSet>>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => JsonSerializer.Deserialize<Dictionary<string, ChannelMuteSet>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));

        builder.Property(x => x.QuietHoursStart).HasColumnType("time without time zone");
        builder.Property(x => x.QuietHoursEnd).HasColumnType("time without time zone");
        builder.Property(x => x.TimeZone).HasMaxLength(64);

        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasDatabaseName("idx_notification_pref_user_id");
    }
}
