namespace Subscriptions.Data.Configurations;

public class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
    {
        builder.ToTable("subscription_invoices");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.PaymentId);
        builder.Property(x => x.OriginalPlanId);
        builder.Property(x => x.InvoiceNumber).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Type).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TaxRate).HasPrecision(5, 4).IsRequired().HasDefaultValue(0.15m);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.PdfStorageKey).HasMaxLength(500);
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);
        builder.Property(x => x.IssuedAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => new { x.Status, x.IsVoided });
    }
}
