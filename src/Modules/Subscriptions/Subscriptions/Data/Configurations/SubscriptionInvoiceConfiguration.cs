using Subscriptions.Domains.Subscriptions.Billing;

namespace Subscriptions.Data.Configurations;

public class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    /// <summary>One invoice (non credit note) per payment: issuing twice for the same payment is a 409.</summary>
    public const string OnePerPaymentIndex = "ux_subscription_invoices_payment";

    public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
    {
        builder.ToTable("subscription_invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.SubscriberId).IsRequired();
        builder.Property(x => x.PaymentId);
        builder.Property(x => x.OriginalInvoiceId);
        builder.Property(x => x.InvoiceNumber).IsRequired().HasMaxLength(InvoiceNumberFormat.MaxLength);
        builder.Property(x => x.Type).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Description).IsRequired().HasMaxLength(500);
        builder.Property(x => x.CreditReason).HasMaxLength(500);

        builder.Property(x => x.PlanId).IsRequired();
        builder.Property(x => x.PlanName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.BillingCycleInMonths).IsRequired();

        builder.Property(x => x.BuyerName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.BuyerEmail).HasMaxLength(256);
        builder.Property(x => x.BuyerAddress).HasMaxLength(500);
        builder.Property(x => x.BuyerTaxId).HasMaxLength(100);
        builder.Property(x => x.SellerName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SellerAddress).HasMaxLength(500);
        builder.Property(x => x.SellerTaxId).HasMaxLength(100);

        // Tax snapshot: no database defaults (the rate comes from configuration at issue time, never a constant).
        builder.Property(x => x.NetAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TaxAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TaxRate).HasPrecision(7, 4).IsRequired();
        builder.Property(x => x.PricesIncludeTax).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreditedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.CouponCode).HasMaxLength(50).IsRequired(false);
        builder.Property(x => x.CouponDiscountAmount).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);

        builder.Property(x => x.PdfStorageKey).HasMaxLength(500);
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);
        builder.Property(x => x.IssuedAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.Ignore(x => x.IsCreditNote);
        builder.Ignore(x => x.IsFullyCredited);
        builder.Ignore(x => x.Amounts);

        builder.HasOne(x => x.Subscription)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SubscriptionInvoice>()
            .WithMany()
            .HasForeignKey(x => x.OriginalInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.SubscriberId);
        builder.HasIndex(x => x.OriginalInvoiceId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PaymentId)
            .IsUnique()
            .HasFilter("\"payment_id\" IS NOT NULL AND \"type\" <> 3")
            .HasDatabaseName(OnePerPaymentIndex);
    }
}
