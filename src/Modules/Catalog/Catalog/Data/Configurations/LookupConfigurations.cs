using Shared.Core.MultiTenancy.Data;

namespace Catalog.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public const string SlugIndexName = "ux_categories_slug";

    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);

        builder.OwnsOne(c => c.Name, name =>
        {
            name.Property(n => n.En).HasColumnName("name_en").IsRequired().HasMaxLength(Category.NameMaxLength);
            name.Property(n => n.Ar).HasColumnName("name_ar").IsRequired().HasMaxLength(Category.NameMaxLength);
        });
        builder.Navigation(c => c.Name).IsRequired();

        builder.Property(c => c.Slug).IsRequired().HasMaxLength(Category.SlugMaxLength);
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.DisplayOrder).IsRequired();

        // Self-reference: optional parent. Restrict — categories are deactivated, never deleted.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Slugs are unique per tenant (tenant_id first: also serves the tenant filter).
        builder.HasIndex(c => new { c.TenantId, c.Slug }).IsUnique().HasDatabaseName(SlugIndexName);
        builder.HasIndex(c => c.ParentId);
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public const string NameIndexName = "ux_tags_name_en";

    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.HasKey(t => t.Id);

        builder.OwnsOne(t => t.Name, name =>
        {
            name.Property(n => n.En).HasColumnName("name_en").IsRequired().HasMaxLength(Tag.NameMaxLength);
            name.Property(n => n.Ar).HasColumnName("name_ar").IsRequired().HasMaxLength(Tag.NameMaxLength);
            // Unique per tenant: ux_..._name_en on (tenant_id, name_en). An owned-type index cannot include the owner's
            // tenant_id, so the index is created by raw SQL in the AddMultiTenancy migration (same name, so
            // IsUniqueViolation(NameIndexName) keeps working).
        });
        builder.Navigation(t => t.Name).IsRequired();

        builder.Property(t => t.IsActive).IsRequired();
    }
}

public class AttributeTypeConfiguration : IEntityTypeConfiguration<AttributeType>
{
    public const string NameIndexName = "ux_attribute_types_name_en";

    public void Configure(EntityTypeBuilder<AttributeType> builder)
    {
        builder.HasKey(a => a.Id);

        builder.OwnsOne(a => a.Name, name =>
        {
            name.Property(n => n.En).HasColumnName("name_en").IsRequired()
                .HasMaxLength(AttributeType.NameMaxLength);
            name.Property(n => n.Ar).HasColumnName("name_ar").IsRequired()
                .HasMaxLength(AttributeType.NameMaxLength);
            // Unique per tenant: ux_..._name_en on (tenant_id, name_en). An owned-type index cannot include the owner's
            // tenant_id, so the index is created by raw SQL in the AddMultiTenancy migration (same name, so
            // IsUniqueViolation(NameIndexName) keeps working).
        });
        builder.Navigation(a => a.Name).IsRequired();

        builder.Property(a => a.DataType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Unit).HasMaxLength(AttributeType.UnitMaxLength);
        builder.Property(a => a.ApplicableTypes).HasColumnType("integer[]");
        builder.Property(a => a.IsActive).IsRequired();
    }
}

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public const string UserProductIndexName = "ux_wishlist_items_user_product";

    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.UserId).IsRequired();
        builder.Property(w => w.ProductId).IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();

        builder.HasOne(w => w.Product)
            .WithMany()
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // A user can wishlist a product only once.
        builder.HasIndex(w => new { w.TenantId, w.UserId, w.ProductId }).IsUnique().HasDatabaseName(UserProductIndexName);

        // Follow the parent's soft delete: items of a soft-deleted product disappear (and come back on restore).
        builder.HasQueryFilter(QueryFilterNames.SoftDelete, w => !w.Product.IsDeleted);
    }
}
