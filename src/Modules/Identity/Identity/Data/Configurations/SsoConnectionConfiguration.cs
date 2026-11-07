using Identity.Domains.Sso.Models;

namespace Identity.Data.Configurations;

public class SsoConnectionConfiguration : IEntityTypeConfiguration<SsoConnection>
{
    public void Configure(EntityTypeBuilder<SsoConnection> entity)
    {
        entity.ToTable("sso_connections");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();
        entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_sso_connections_tenant_id");

        entity.Property(e => e.Name).HasMaxLength(SsoConnection.NameMaxLength).IsRequired();
        entity.Property(e => e.Protocol).HasConversion<string>().HasMaxLength(16);
        entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);

        entity.Property(e => e.OidcAuthority).HasMaxLength(SsoConnection.UrlMaxLength);
        entity.Property(e => e.OidcClientId).HasMaxLength(512);
        entity.Property(e => e.OidcClientSecretProtected).HasMaxLength(4096);
        entity.Property(e => e.OidcExtraScopes).HasMaxLength(512);

        entity.Property(e => e.SamlIdpEntityId).HasMaxLength(SsoConnection.UrlMaxLength);
        entity.Property(e => e.SamlIdpSsoUrl).HasMaxLength(SsoConnection.UrlMaxLength);
        entity.Property(e => e.SamlIdpMetadataUrl).HasMaxLength(SsoConnection.UrlMaxLength);
        entity.Property(e => e.SamlIdpCertificates).HasColumnType("text[]");
        entity.Property(e => e.SamlMetadataLastError).HasMaxLength(64);

        entity.Property(e => e.EmailClaim).HasMaxLength(SsoConnection.ClaimMaxLength);
        entity.Property(e => e.GivenNameClaim).HasMaxLength(SsoConnection.ClaimMaxLength);
        entity.Property(e => e.FamilyNameClaim).HasMaxLength(SsoConnection.ClaimMaxLength);
        entity.Property(e => e.DefaultRoleIds).HasColumnType("uuid[]");

        entity.HasMany(e => e.Domains)
            .WithOne()
            .HasForeignKey(d => d.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(e => e.Domains).HasField("_domains").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class SsoDomainConfiguration : IEntityTypeConfiguration<SsoDomain>
{
    public void Configure(EntityTypeBuilder<SsoDomain> entity)
    {
        entity.ToTable("sso_domains");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();

        entity.Property(e => e.Domain).HasMaxLength(253).IsRequired();
        entity.Property(e => e.VerificationToken).HasMaxLength(64).IsRequired();
        entity.Ignore(e => e.IsVerified);
        entity.Ignore(e => e.RecordName);
        entity.Ignore(e => e.RecordValue);

        entity.HasIndex(e => new { e.ConnectionId, e.Domain }).IsUnique().HasDatabaseName("ux_sso_domains_connection_domain");

        // A domain can be VERIFIED by exactly one connection platform-wide: the home-realm lookup must be unambiguous
        // and one tenant must never take over another tenant's users.
        entity.HasIndex(e => e.Domain).IsUnique().HasFilter("verified_at IS NOT NULL")
            .HasDatabaseName("ux_sso_domains_verified_domain");
    }
}
