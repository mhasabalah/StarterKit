namespace Subscriptions.Data.Configurations;

public class SubscriptionTransitionLogConfiguration
    : IEntityTypeConfiguration<SubscriptionTransitionLog>
{
    public void Configure(EntityTypeBuilder<SubscriptionTransitionLog> builder)
    {
        builder.ToTable("subscription_transition_logs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Reason)
            .HasMaxLength(1000);

        builder.Property(x => x.ToStatus)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.FromStatus)
            .HasConversion<int?>();

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.HasIndex(x => x.SubscriptionId);
        builder.HasIndex(x => x.SubscriberId);
    }
}
