using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Models;

namespace Subscriptions.Data.Configurations;

public class SubscriptionRefundRequestConfiguration : IEntityTypeConfiguration<SubscriptionRefundRequest>
{
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
        builder.Property(x => x.RequestedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.RefundType).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.RequestedAt).IsRequired();
        builder.Property(x => x.ReviewedBy).IsRequired(false);
        builder.Property(x => x.ReviewedAt).IsRequired(false);
        builder.Property(x => x.ReviewNotes).HasMaxLength(1000).IsRequired(false);
        builder.Property(x => x.ProcessedAt).IsRequired(false);
        builder.Property(x => x.ExternalTransactionRef).HasMaxLength(200).IsRequired(false);
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.SubscriberId);
        builder.HasIndex(x => x.Status);

        // At most one active (non-terminal) refund per subscription
        builder.HasIndex(x => x.SubscriptionId)
            .IsUnique()
            .HasFilter("\"status\" IN (0, 1, 2)")
            .HasDatabaseName("ix_subscription_refund_requests_subscription_active");
    }
}
