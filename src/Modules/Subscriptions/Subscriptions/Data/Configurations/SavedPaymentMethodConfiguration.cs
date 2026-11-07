using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Configurations;

public class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public const string OneDefaultPerSubscriberIndex = "ux_saved_payment_methods_default_per_subscriber";
    public const string OnePerProviderMethodIndex = "ux_saved_payment_methods_gateway_method";

    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("saved_payment_methods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.SubscriberId).IsRequired();
        builder.Property(x => x.Gateway).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ProviderCustomerRef).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ProviderMethodRef).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(30);
        builder.Property(x => x.Last4).HasMaxLength(4);
        builder.Property(x => x.IsDefault).IsRequired();
        builder.Property(x => x.ConsentedAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Ignore(x => x.IsActive);

        builder.HasIndex(x => new { x.Gateway, x.ProviderMethodRef }).IsUnique().HasDatabaseName(OnePerProviderMethodIndex);
        builder.HasIndex(x => x.SubscriberId, "subscriber").HasDatabaseName("ix_saved_payment_methods_subscriber_id");
        // One default among the subscriber's ACTIVE methods.
        builder.HasIndex(x => x.SubscriberId, "one_default_per_subscriber")
            .IsUnique()
            .HasDatabaseName(OneDefaultPerSubscriberIndex)
            .HasFilter("is_default AND revoked_at IS NULL");
    }
}
