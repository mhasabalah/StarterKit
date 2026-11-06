using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Configurations;

public class OnlinePaymentRefundConfiguration : IEntityTypeConfiguration<OnlinePaymentRefund>
{
    public void Configure(EntityTypeBuilder<OnlinePaymentRefund> builder)
    {
        builder.ToTable("online_payment_refunds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ProviderRefundId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasMoneyPrecision().IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // The idempotency key of a refund: booked once, however many events / calls report it.
        builder.HasIndex(x => new { x.Provider, x.ProviderRefundId }).IsUnique();
        builder.HasIndex(x => x.SessionId);
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.RefundRequestId);
    }
}
