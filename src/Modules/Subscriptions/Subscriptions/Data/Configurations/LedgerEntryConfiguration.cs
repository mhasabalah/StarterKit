using Microsoft.EntityFrameworkCore.Metadata;

namespace Subscriptions.Data.Configurations;

/// <summary>
/// Append-only money ledger. Every property is marked <see cref="PropertySaveBehavior.Throw"/> after save, so EF
/// refuses to UPDATE a ledger row; <see cref="SubscriptionsDbContext"/> also refuses Modified/Deleted entries.
/// Optional DB-level guard (raw SQL, not generated): see <c>wiki/Subscriptions/Billing.md</c> (Ledger).
/// </summary>
public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasMoneyPrecision().IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.SubscriberId).IsRequired();
        builder.Property(x => x.ActorId).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PlanName).HasMaxLength(200);
        builder.Property(x => x.PaymentMethod).HasMaxLength(100);
        builder.Property(x => x.Reference).HasMaxLength(200);

        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }

        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => new { x.Currency, x.OccurredAt });
        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.SubscriberId);
        builder.HasIndex(x => x.PaymentId);
        builder.HasIndex(x => x.RefundId);
    }
}
