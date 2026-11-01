namespace Subscriptions.Data.Configurations;

public class ScheduledPlanChangeConfiguration : IEntityTypeConfiguration<ScheduledPlanChange>
{
    public void Configure(EntityTypeBuilder<ScheduledPlanChange> builder)
    {
        builder.ToTable("scheduled_plan_changes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.CurrentPlanId).IsRequired();
        builder.Property(x => x.NewPlanId).IsRequired();
        builder.Property(x => x.NewPlanName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NewPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.NewBillingCycleInMonths).IsRequired();
        builder.Property(x => x.NewFeaturesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ChangeType).HasConversion<int>().IsRequired();
        builder.Property(x => x.ScheduledBy).IsRequired();
        builder.Property(x => x.ScheduledAt).IsRequired();
        builder.Property(x => x.EffectiveAt).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => new { x.SubscriptionId, x.Status }).IsUnique();
    }
}
