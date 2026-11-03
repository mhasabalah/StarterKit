using Microsoft.EntityFrameworkCore.Metadata;

namespace Subscriptions.Data.Configurations;

/// <summary>Manual (offline) receipts of a payment. Insert-only, like the ledger.</summary>
public class SubscriptionPaymentReceiptConfiguration : IEntityTypeConfiguration<SubscriptionPaymentReceipt>
{
    public void Configure(EntityTypeBuilder<SubscriptionPaymentReceipt> builder)
    {
        builder.ToTable("subscription_payment_receipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(200);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.RecordedAt).IsRequired();

        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        builder.HasIndex(x => x.PaymentId);
        builder.HasIndex(x => x.SubscriptionId);
    }
}
