namespace Catalog.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(Product.NameMaxLength);

        builder.Property(p => p.Description)
            .HasMaxLength(Product.DescriptionMaxLength);

        builder.Property(p => p.Sku)
            .IsRequired()
            .HasMaxLength(Product.SkuMaxLength);

        builder.Property(p => p.Price)
            .HasPrecision(18, 2);

        // NOTE: no HasDefaultValue(true) on IsActive — for bool, EF treats `false` as
        // "unset" and would let the DB default silently turn inactive products active.
        builder.Property(p => p.IsActive).IsRequired();

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Name);
    }
}
