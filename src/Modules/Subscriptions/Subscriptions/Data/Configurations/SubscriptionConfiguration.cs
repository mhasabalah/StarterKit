namespace Subscriptions.Data.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubscriberId)
            .IsRequired();

        builder.Property(x => x.PlanId)
            .IsRequired();

        builder.Property(x => x.PlanName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Price)
            .HasPrecision(18, 2);

        builder.Property(x => x.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(x => x.BillingCycleInMonths)
            .IsRequired();

        builder.Property(x => x.FeaturesJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.IdempotencyToken)
            .HasMaxLength(200);

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasMany(x => x.Payments)
            .WithOne(x => x.Subscription)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Invoices)
            .WithOne(x => x.Subscription)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ScheduledPlanChange)
            .WithOne()
            .HasForeignKey<ScheduledPlanChange>(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.CancelledAtPeriodEnd)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.ScheduledCancellationDate)
            .IsRequired(false);

        builder.Property(x => x.CancellationReason)
            .HasConversion<int?>()
            .IsRequired(false);

        builder.Property(x => x.CancellationNote)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.HasIndex(x => x.SubscriberId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RenewalDate);
        builder.HasIndex(x => x.GracePeriodEndsAt);
        builder.HasIndex(x => new { x.Status, x.CancelledAtPeriodEnd, x.ScheduledCancellationDate })
            .HasFilter("\"cancelled_at_period_end\" = true");
    }
}
