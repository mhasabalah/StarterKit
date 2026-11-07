using Identity.Domains.Scim.Models;

namespace Identity.Data.Configurations;

public class ScimDirectoryConfiguration : IEntityTypeConfiguration<ScimDirectory>
{
    public void Configure(EntityTypeBuilder<ScimDirectory> entity)
    {
        entity.ToTable("scim_directories");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.Property(e => e.Name).HasMaxLength(ScimDirectory.NameMaxLength).IsRequired();
        entity.Property(e => e.TokenPrefix).HasMaxLength(16).IsRequired();
        entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
        entity.HasIndex(e => e.TokenPrefix).IsUnique().HasDatabaseName("ux_scim_directories_token_prefix");
        entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_scim_directories_tenant_id");
    }
}

public class ScimUserConfiguration : IEntityTypeConfiguration<ScimUser>
{
    public void Configure(EntityTypeBuilder<ScimUser> entity)
    {
        entity.ToTable("scim_users");
        entity.HasKey(e => new { e.DirectoryId, e.UserId });
        entity.Property(e => e.UserName).HasMaxLength(256).IsRequired();
        entity.Property(e => e.ExternalId).HasMaxLength(256);
        entity.Property(e => e.EmployeeNumber).HasMaxLength(ScimEnterprise.MaxLength);
        entity.Property(e => e.CostCenter).HasMaxLength(ScimEnterprise.MaxLength);
        entity.Property(e => e.Organization).HasMaxLength(ScimEnterprise.MaxLength);
        entity.Property(e => e.Division).HasMaxLength(ScimEnterprise.MaxLength);
        entity.Property(e => e.Department).HasMaxLength(ScimEnterprise.MaxLength);
        entity.Ignore(e => e.Enterprise);

        // Optimistic concurrency: parallel PATCHes from the IdP must not overwrite each other.
        entity.Property(e => e.Revision).IsConcurrencyToken();

        // SCIM uniqueness (409 scimType=uniqueness) is per directory; the DB index settles concurrent creates.
        entity.HasIndex(e => new { e.DirectoryId, e.UserName }).IsUnique().HasDatabaseName("ux_scim_users_directory_user_name");
        entity.HasIndex(e => new { e.DirectoryId, e.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL")
            .HasDatabaseName("ux_scim_users_directory_external_id");

        entity.HasOne<ScimDirectory>().WithMany().HasForeignKey(e => e.DirectoryId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ScimGroupConfiguration : IEntityTypeConfiguration<ScimGroup>
{
    public void Configure(EntityTypeBuilder<ScimGroup> entity)
    {
        entity.ToTable("scim_groups");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.Property(e => e.DisplayName).HasMaxLength(256).IsRequired();
        entity.Property(e => e.ExternalId).HasMaxLength(256);
        entity.Property(e => e.Members).HasColumnType("uuid[]");

        // Okta/Entra send many small parallel membership PATCHes; a stale read-modify-write must fail, not win.
        entity.Property(e => e.Revision).IsConcurrencyToken();
        entity.HasIndex(e => new { e.DirectoryId, e.DisplayName }).IsUnique().HasDatabaseName("ux_scim_groups_directory_display_name");
        entity.HasOne<ScimDirectory>().WithMany().HasForeignKey(e => e.DirectoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
