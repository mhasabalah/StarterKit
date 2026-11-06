using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Configurations;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public const string OnePerStepIndex = "ux_payment_attempts_payment_step";

    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.PaymentId).IsRequired();
        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.Step).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasMoneyPrecision().IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(200);
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.PaymentId, x.Step }).IsUnique().HasDatabaseName(OnePerStepIndex);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.SubscriptionId);
    }
}
