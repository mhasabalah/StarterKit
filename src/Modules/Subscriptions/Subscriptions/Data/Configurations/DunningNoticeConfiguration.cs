namespace Subscriptions.Data.Configurations;

public class DunningNoticeConfiguration : IEntityTypeConfiguration<DunningNotice>
{
    public const string OnePerStepIndex = "ux_dunning_notices_payment_step";

    public void Configure(EntityTypeBuilder<DunningNotice> builder)
    {
        builder.ToTable("dunning_notices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.PaymentId).IsRequired();
        builder.Property(x => x.Step).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Sent).IsRequired();
        builder.Property(x => x.RecordedAt).IsRequired();
        builder.Property(x => x.AutoRetryOutcome).HasMaxLength(20);

        builder.HasIndex(x => new { x.PaymentId, x.Step }).IsUnique().HasDatabaseName(OnePerStepIndex);
        builder.HasIndex(x => x.SubscriptionId);
    }
}
