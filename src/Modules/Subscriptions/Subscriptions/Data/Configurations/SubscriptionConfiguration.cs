namespace Subscriptions.Data.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    /// <summary>At most one open (PendingApproval / Active / PastDue) subscription per subscriber (C12).</summary>
    public const string OpenPerSubscriberIndex = "ux_subscriptions_subscriber_open";

    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SubscriberId).IsRequired();
        builder.Property(x => x.SubscriberDisplayName).HasMaxLength(200);
        builder.Property(x => x.SubscriberEmail).HasMaxLength(256);
        builder.Property(x => x.ComplimentaryUntil).IsRequired(false);
        builder.Property(x => x.PlanId).IsRequired();
        builder.Property(x => x.PlanName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.DeferredProrationAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.BillingCycleInMonths).IsRequired();
        builder.Property(x => x.FeaturesJson).HasColumnType("jsonb").IsRequired();

        // Stored by name: the enum used to exist twice with conflicting numbers; names are unambiguous.
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.CurrentPeriodStart).IsRequired(false);

        builder.HasMany(x => x.Payments)
            .WithOne(x => x.Subscription)
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ScheduledPlanChanges)
            .WithOne()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.PendingPlanChange);
        builder.Ignore(x => x.EffectivePeriodStart);
        builder.Ignore(x => x.ActivationPayment);

        builder.Property(x => x.CancelledAtPeriodEnd).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.ScheduledCancellationDate).IsRequired(false);
        builder.Property(x => x.CancellationReason).HasConversion<int?>().IsRequired(false);
        builder.Property(x => x.CancellationNote).HasMaxLength(1000).IsRequired(false);

        builder.HasIndex(x => x.SubscriberId);
        builder.HasIndex(x => x.SubscriberId)
            .IsUnique()
            .HasFilter("\"status\" IN ('PendingApproval', 'Active', 'PastDue')")
            .HasDatabaseName(OpenPerSubscriberIndex);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PlanId);
        builder.HasIndex(x => x.SubscriberEmail);
        builder.HasIndex(x => new { x.Status, x.RenewalDate });
        builder.HasIndex(x => new { x.Status, x.GracePeriodEndsAt });
    }
}
