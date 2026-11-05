using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Configurations;

public class OnlinePaymentSessionConfiguration : IEntityTypeConfiguration<OnlinePaymentSession>
{
    public void Configure(EntityTypeBuilder<OnlinePaymentSession> builder)
    {
        builder.ToTable("online_payment_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.PaymentId).IsRequired();
        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProviderPaymentReference).HasMaxLength(200);
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // The webhook finds its payment through this key.
        builder.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique();
        builder.HasIndex(x => x.PaymentId);
        builder.HasIndex(x => new { x.Provider, x.ProviderPaymentReference });
    }
}
