namespace Catalog.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <summary>Explicit name so the repository can recognise this unique violation (→ 409 SKU conflict).</summary>
    public const string SkuIndexName = "ux_products_sku";

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        // Owned value object #1: bilingual name → name_en / name_ar columns.
        builder.OwnsOne(p => p.Name, name =>
        {
            name.Property(n => n.En).HasColumnName("name_en").IsRequired().HasMaxLength(Product.NameMaxLength);
            name.Property(n => n.Ar).HasColumnName("name_ar").IsRequired().HasMaxLength(Product.NameMaxLength);
            name.HasIndex(n => n.En);
        });
        builder.Navigation(p => p.Name).IsRequired();

        // Owned value object #2: pricing with a computed (ignored) DiscountPercentage.
        builder.OwnsOne(p => p.Pricing, pricing =>
        {
            pricing.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 2).IsRequired();
            pricing.Property(x => x.CompareAtPrice).HasColumnName("compare_at_price").HasPrecision(18, 2);
            pricing.Property(x => x.Currency).HasColumnName("currency").HasConversion<string>()
                .HasMaxLength(10).IsRequired();
            pricing.Ignore(x => x.DiscountPercentage);
            pricing.HasIndex(x => x.Price);
        });
        builder.Navigation(p => p.Pricing).IsRequired();

        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Property(p => p.Sku).IsRequired().HasMaxLength(Product.SkuMaxLength);

        // Enums stored as strings (readable, reorder-safe).
        builder.Property(p => p.Type).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Status).IsRequired().HasConversion<string>().HasMaxLength(30);

        builder.Property(p => p.ReviewNotes).HasMaxLength(Product.ReviewNotesMaxLength);
        builder.Property(p => p.ViewCount).IsRequired();

        // No HasDefaultValue on bools: EF treats `false` as "unset" and the DB default would win.
        builder.Property(p => p.IsFeatured).IsRequired();

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Images)
            .WithOne()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Attributes)
            .WithOne()
            .HasForeignKey(a => a.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Tags)
            .WithOne()
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(p => p.PrimaryImage);
        builder.Ignore(p => p.CanAddImage);

        // SKU is unique across ALL rows, soft-deleted included (a restore must never collide).
        // SKUs are unique per tenant.
        builder.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique().HasDatabaseName(SkuIndexName);
        builder.HasIndex(p => new { p.TenantId, p.BranchId });
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.OwnerId);
        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => p.IsFeatured);
        builder.HasIndex(p => p.CreatedAt);
        // Tenant-first, deterministic order for streaming exports (DataTransfer catalog.products): the sort is an index scan.
        builder.HasIndex(p => new { p.TenantId, p.CreatedAt, p.Id });
        builder.HasIndex(p => new { p.IsDeleted, p.DeletedAt });
    }
}
