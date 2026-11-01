using Subscriptions.Domains.Plans.Models;

namespace Subscriptions.Data.Configurations;

public class PlanFeatureDefinitionConfiguration : IEntityTypeConfiguration<PlanFeatureDefinition>
{
    public void Configure(EntityTypeBuilder<PlanFeatureDefinition> builder)
    {
        builder.ToTable("plan_feature_definitions");
        builder.HasKey(x => x.Key);

        builder.Property(x => x.Key).HasMaxLength(100).IsRequired();

        builder.OwnsOne(x => x.DisplayName, b =>
        {
            b.Property(x => x.En).HasColumnName("display_name_en").HasMaxLength(200).IsRequired();
            b.Property(x => x.Ar).HasColumnName("display_name_ar").HasMaxLength(200).IsRequired();
        });

        builder.OwnsOne(x => x.Description, b =>
        {
            b.Property(x => x.En).HasColumnName("description_en").HasMaxLength(500).IsRequired(false);
            b.Property(x => x.Ar).HasColumnName("description_ar").HasMaxLength(500).IsRequired(false);
        });

        builder.Property(x => x.Category).HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
