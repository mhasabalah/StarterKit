namespace Subscriptions.Data.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(x => x.Id);

        builder.OwnsOne(x => x.Name, b =>
        {
            b.Property(x => x.En).HasColumnName("name_en").HasMaxLength(100).IsRequired();
            b.Property(x => x.Ar).HasColumnName("name_ar").HasMaxLength(100).IsRequired();
        });

        builder.OwnsOne(x => x.Description, b =>
        {
            b.Property(x => x.En).HasColumnName("description_en").HasMaxLength(500).IsRequired(false);
            b.Property(x => x.Ar).HasColumnName("description_ar").HasMaxLength(500).IsRequired(false);
        });

        builder.Property(x => x.Price).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("USD").IsRequired();
        builder.Property(x => x.BillingCycleInMonths).IsRequired();
        builder.Property(x => x.SeedKey).HasMaxLength(100);
        builder.Property(x => x.DeletedAt).IsRequired(false);
        builder.Property(x => x.PendingPrice).HasPrecision(18, 2).IsRequired(false);
        builder.Property(x => x.PriceEffectiveAt).IsRequired(false);
        builder.Property(x => x.PriceChangeAnnouncedAt).IsRequired(false);
        builder.Property(x => x.DiscountPercent).HasPrecision(5, 2).IsRequired(false);

        builder.HasIndex(x => x.SeedKey)
            .IsUnique()
            .HasFilter("\"seed_key\" IS NOT NULL");

        builder.HasMany(x => x.Features)
            .WithOne()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
