namespace Catalog.Data.Configurations;

// Child ids are generated in the domain (Guid.NewGuid) and marked ValueGeneratedNever, so a child added
// to an already-tracked product is INSERTed (EF would otherwise assume "key set ⇒ existing row").

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Url).IsRequired().HasMaxLength(ProductImage.UrlMaxLength);
        builder.Property(i => i.Caption).HasMaxLength(ProductImage.CaptionMaxLength);
        builder.Property(i => i.IsPrimary).IsRequired();
        builder.Property(i => i.DisplayOrder).IsRequired();
        builder.Property(i => i.AttachmentId).IsRequired();

        builder.HasIndex(i => new { i.TenantId, i.ProductId, i.AttachmentId }).IsUnique();
        builder.HasIndex(i => new { i.ProductId, i.IsPrimary });
    }
}

public class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Value).IsRequired().HasMaxLength(ProductAttribute.ValueMaxLength);

        builder.HasOne(a => a.AttributeType)
            .WithMany()
            .HasForeignKey(a => a.AttributeTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.TenantId, a.ProductId, a.AttributeTypeId }).IsUnique();
        builder.HasIndex(a => a.AttributeTypeId);
    }
}

public class ProductTagConfiguration : IEntityTypeConfiguration<ProductTag>
{
    public void Configure(EntityTypeBuilder<ProductTag> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.HasOne(t => t.Tag)
            .WithMany()
            .HasForeignKey(t => t.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.TenantId, t.ProductId, t.TagId }).IsUnique();
        builder.HasIndex(t => t.TagId);
    }
}
