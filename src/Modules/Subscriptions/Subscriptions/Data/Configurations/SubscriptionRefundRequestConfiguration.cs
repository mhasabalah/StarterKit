namespace Subscriptions.Data.Configurations;

public class SubscriptionRefundRequestConfiguration : IEntityTypeConfiguration<SubscriptionRefundRequest>
{
    /// <summary>
    /// At most one non-rejected refund per subscription: Requested(0), UnderReview(1), Approved(2) AND Processed(4).
    /// Including Processed makes a second refund after a processed one impossible at the database level (C11).
    /// </summary>
    public const string OneRefundPerSubscriptionIndex = "ix_subscription_refund_requests_subscription_active";

    public void Configure(EntityTypeBuilder<SubscriptionRefundRequest> builder)
    {
        builder.ToTable("subscription_refund_requests");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.SubscriberId).IsRequired();
        builder.Property(x => x.PlanId).IsRequired();
        builder.Property(x => x.PlanName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.BillingCycleInMonths).IsRequired();
        builder.Property(x => x.SubscriptionStartedAt).IsRequired();
        builder.Property(x => x.CancelledAt).IsRequired();
        builder.Property(x => x.RenewalDate).IsRequired();
        builder.Property(x => x.RequestedAmount).HasMoneyPrecision().IsRequired();
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.RefundType).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.RequestedAt).IsRequired();
        builder.Property(x => x.ReviewedBy).IsRequired(false);
        builder.Property(x => x.ReviewedAt).IsRequired(false);
        builder.Property(x => x.ReviewNotes).HasMaxLength(1000).IsRequired(false);
        builder.Property(x => x.ProcessedAt).IsRequired(false);
        builder.Property(x => x.ExternalTransactionRef).HasMaxLength(200).IsRequired(false);
        builder.Property(x => x.ProcessingStartedAt).IsRequired(false);
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.SubscriberId);
        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.SubscriptionId)
            .IsUnique()
            .HasFilter("\"status\" IN (0, 1, 2, 4)")
            .HasDatabaseName(OneRefundPerSubscriptionIndex);
    }
}
