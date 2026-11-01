namespace Subscriptions.Data.Configurations;

public class PaymentInstructionsConfiguration : IEntityTypeConfiguration<PaymentInstructions>
{
    public void Configure(EntityTypeBuilder<PaymentInstructions> builder)
    {
        builder.ToTable("payment_instructions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SupportWhatsApp).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SupportEmail).HasMaxLength(256).IsRequired();
    }
}
