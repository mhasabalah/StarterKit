namespace Subscriptions.Data.Configurations;

public class SubscriptionPaymentConfiguration
    : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("subscription_payments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubscriptionId)
            .IsRequired();

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.PaymentMethodId);

        builder.Property(x => x.PaymentMethodType)
            .HasMaxLength(100);

        builder.Property(x => x.PaymentMethodDisplayName)
            .HasMaxLength(200);

        builder.Property(x => x.PaymentMethodValue)
            .HasMaxLength(500);

        builder.Property(x => x.PaymentMethodDescription)
            .HasMaxLength(1000);

        builder.Property(x => x.ProofAttachmentId);

        builder.Property(x => x.Notes)
            .HasMaxLength(1000);

        builder.Property(x => x.IdempotencyToken)
            .HasMaxLength(200);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PaymentMethodId);
    }
}
