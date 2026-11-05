using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Configurations;

public class InboundWebhookEventConfiguration : IEntityTypeConfiguration<InboundWebhookEvent>
{
    public void Configure(EntityTypeBuilder<InboundWebhookEvent> builder)
    {
        builder.ToTable("inbound_webhook_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EventId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ProviderReference).HasMaxLength(200);
        builder.Property(x => x.ProviderRefundId).HasMaxLength(200);
        builder.Property(x => x.PaymentReference).HasMaxLength(200);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReceivedAt).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1000);

        // The idempotency key: a re-delivered event hits ON CONFLICT DO NOTHING and is never reprocessed.
        builder.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.Attempts, x.LastAttemptAt });
    }
}
