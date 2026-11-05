using Tenancy.Domains.FeatureFlags.Models;

namespace Tenancy.Data.Configurations;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public const string KeyIndexName = "ux_feature_flags_key";

    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("feature_flags",
            t => t.HasCheckConstraint("ck_feature_flags_rollout", "rollout_percent BETWEEN 0 AND 100"));
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Key).IsRequired().HasMaxLength(FeatureFlag.KeyMaxLength);
        builder.Property(f => f.Description).HasMaxLength(FeatureFlag.DescriptionMaxLength);
        builder.HasIndex(f => f.Key).IsUnique().HasDatabaseName(KeyIndexName);

        builder.HasMany(f => f.Overrides).WithOne().HasForeignKey(o => o.FeatureFlagId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Overrides).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(f => f.DomainEvents);
        builder.Ignore(f => f.IntegrationEvents);
    }
}

public class FeatureFlagOverrideConfiguration : IEntityTypeConfiguration<FeatureFlagOverride>
{
    public const string TenantIndexName = "ux_feature_flag_overrides_tenant";
    public const string UserIndexName = "ux_feature_flag_overrides_user";

    public void Configure(EntityTypeBuilder<FeatureFlagOverride> builder)
    {
        builder.ToTable("feature_flag_overrides", t => t.HasCheckConstraint(
            "ck_feature_flag_overrides_one_target", "(tenant_id IS NULL) <> (user_id IS NULL)"));
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever(); // client-generated child key

        // One override per (flag, tenant) and per (flag, user); exactly one target per row.
        builder.HasIndex(o => new { o.FeatureFlagId, o.TenantId }).IsUnique().HasDatabaseName(TenantIndexName)
            .HasFilter("tenant_id IS NOT NULL");
        builder.HasIndex(o => new { o.FeatureFlagId, o.UserId }).IsUnique().HasDatabaseName(UserIndexName)
            .HasFilter("user_id IS NOT NULL");
    }
}
