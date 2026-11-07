using ModuleName.Domains.Items.Models;

namespace ModuleName.Data.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    /// <summary>Unique code (per tenant for tenant data): the natural key, and the name the handlers catch on a race.</summary>
    public const string CodeIndexName = "ux_items_code";

    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever(); // the aggregate creates its id

        builder.Property(x => x.Code).HasMaxLength(Item.CodeMaxLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(Item.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Item.DescriptionMaxLength);

#if (TENANT_DATA)
        // Unique indexes of tenant data start with tenant_id (TenantIsolationArchitectureTests).
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName(CodeIndexName);
#else
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName(CodeIndexName);
#endif
        builder.HasIndex(x => x.CreatedAt);
    }
}
