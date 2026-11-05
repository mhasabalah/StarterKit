namespace Subscriptions.Data.Configurations;

public class SubscriptionPaymentConfiguration
    : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("subscription_payments");
        builder.HasKey(x => x.Id);

        // Client-assigned ids: a payment added through the Subscription.Payments navigation must be INSERTed
        // (EF would otherwise treat a set key as an existing row).
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.IncludedProrationAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.OriginalPlanId).IsRequired(false);
        builder.Property(x => x.CouponCode).HasMaxLength(50).IsRequired(false);
        builder.Property(x => x.CouponDiscountAmount).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.PaymentMethodId);
        builder.Property(x => x.PaymentMethodType).HasMaxLength(100);
        builder.Property(x => x.PaymentMethodDisplayName).HasMaxLength(200);
        builder.Property(x => x.PaymentMethodValue).HasMaxLength(500);
        builder.Property(x => x.PaymentMethodDescription).HasMaxLength(1000);
        builder.Property(x => x.ProofAttachmentId);
        builder.Property(x => x.ProofContentHash).HasMaxLength(64);
        builder.Property(x => x.DuplicateProof).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.DuplicateOfPaymentId);
        builder.Property(x => x.AmountReceived).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);
        builder.Property(x => x.SubmittedAt).IsRequired(false);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.Ignore(x => x.IsAwaitingPayment);
        builder.Ignore(x => x.AcceptsProof);
        builder.Ignore(x => x.OutstandingAmount);

        builder.HasMany(x => x.Receipts)
            .WithOne()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Receipts).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => new { x.SubscriptionId, x.Status });
        builder.HasIndex(x => new { x.Purpose, x.Status, x.CreatedAt });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PaymentMethodId);
        builder.HasIndex(x => x.ProofContentHash);
    }
}
