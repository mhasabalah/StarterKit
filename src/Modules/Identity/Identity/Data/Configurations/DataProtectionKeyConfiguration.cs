using Identity.Infrastructure.DataProtection;

namespace Identity.Data.Configurations;

public class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKeyEntry>
{
    public void Configure(EntityTypeBuilder<DataProtectionKeyEntry> builder)
    {
        builder.ToTable("data_protection_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.FriendlyName).HasMaxLength(200);
        builder.Property(k => k.Xml).IsRequired();
    }
}
