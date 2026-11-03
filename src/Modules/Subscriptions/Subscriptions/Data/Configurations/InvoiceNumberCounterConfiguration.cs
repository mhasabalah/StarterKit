namespace Subscriptions.Data.Configurations;

public class InvoiceNumberCounterConfiguration : IEntityTypeConfiguration<InvoiceNumberCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberCounter> builder)
    {
        builder.ToTable("invoice_number_counters");
        builder.HasKey(x => x.Year);
        builder.Property(x => x.Year).ValueGeneratedNever();
        builder.Property(x => x.LastValue).IsRequired();
    }
}
