namespace Subscriptions.Data.Configurations;

public class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> builder)
    {
        builder.ToTable("plan_features");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlanId).IsRequired();

        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.PlanId, x.Key })
            .IsUnique()
            .HasDatabaseName("ux_plan_features_plan_key");
    }
}
