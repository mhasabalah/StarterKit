using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notification.Contracts.Devices;

namespace Notification.Persistence.Configurations;

public sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> builder)
    {
        builder.ToTable("push_device");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Token).HasMaxLength(RegisterPushDeviceCommand.TokenMaxLength).IsRequired();
        builder.Property(x => x.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.AppVersion).HasMaxLength(RegisterPushDeviceCommand.AppVersionMaxLength);
        builder.Property(x => x.Locale).HasMaxLength(RegisterPushDeviceCommand.LocaleMaxLength);

        // An FCM token identifies ONE app install: it belongs to exactly one user at a time.
        builder.HasIndex(x => x.Token).IsUnique().HasDatabaseName("ux_push_device_token");
        builder.HasIndex(x => new { x.UserId, x.LastSeenAt }).HasDatabaseName("idx_push_device_user_last_seen");
        builder.HasIndex(x => x.LastSeenAt).HasDatabaseName("idx_push_device_last_seen");
    }
}
