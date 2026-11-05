using Subscriptions.Domains.Metering.Models;

namespace Subscriptions.Data.Configurations;

public class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SubscriberId).IsRequired();
        builder.Property(x => x.Key).IsRequired().HasMaxLength(100);
        builder.Property(x => x.PeriodKey).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.SubscriberId, x.Key, x.PeriodKey })
            .IsUnique()
            .HasDatabaseName("ux_usage_records_subscriber_key_period");
    }
}
