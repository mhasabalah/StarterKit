using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Tenancy.Data.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public const string IdentifierIndexName = "ux_tenants_identifier";
    public const string DomainHostIndexName = "ux_tenant_domains_host";

    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Identifier).IsRequired().HasMaxLength(Tenant.IdentifierMaxLength);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(Tenant.NameMaxLength);
        builder.Property(t => t.LegalName).HasMaxLength(Tenant.NameMaxLength);
        builder.Property(t => t.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Isolation).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.ConnectionSecretRef).HasMaxLength(128);
        builder.Property(t => t.Region).HasMaxLength(64);
        builder.Property(t => t.Culture).HasMaxLength(16);
        builder.Property(t => t.TimeZone).HasMaxLength(64);
        builder.Property(t => t.Currency).HasMaxLength(3);
        builder.Property(t => t.PrimaryColor).HasMaxLength(16);
        builder.Property(t => t.StatusReason).HasMaxLength(Tenant.ReasonMaxLength);
        builder.Property(t => t.MoveTargetIsolation).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.MoveTargetConnectionSecretRef).HasMaxLength(128);
        builder.Property(t => t.StatusBeforeMove).HasConversion<string>().HasMaxLength(32);
        builder.Ignore(t => t.EffectiveStatus);

        builder.HasIndex(t => t.Identifier).IsUnique().HasDatabaseName(IdentifierIndexName);
        builder.HasIndex(t => t.Status);
        builder.HasIndex(t => t.OwnerUserId);

        builder.OwnsMany(t => t.Domains, domain =>
        {
            domain.ToTable("tenant_domains");
            domain.WithOwner().HasForeignKey("TenantId");
            domain.HasKey(d => d.Id);
            // Ids are assigned in the domain (Guid.CreateVersion7). Without ValueGeneratedNever EF treats a NEW domain
            // found in the collection as existing (generated key already set) and issues an UPDATE that hits 0 rows.
            domain.Property(d => d.Id).ValueGeneratedNever();
            domain.Property(d => d.Host).IsRequired().HasMaxLength(TenantDomain.HostMaxLength);
            domain.Property(d => d.VerificationToken).IsRequired().HasMaxLength(64);
            // A host maps to at most one tenant, platform-wide.
            domain.HasIndex(d => d.Host).IsUnique().HasDatabaseName(DomainHostIndexName);
        });
        builder.Navigation(t => t.Domains).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public const string CodeIndexName = "ux_branches_tenant_code";

    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Code).IsRequired().HasMaxLength(Branch.CodeMaxLength);
        builder.Property(b => b.Name).IsRequired().HasMaxLength(Branch.NameMaxLength);
        builder.Property(b => b.Path).IsRequired().HasMaxLength(Branch.PathMaxLength);
        builder.Property(b => b.Address).HasMaxLength(Branch.AddressMaxLength);
        builder.Property(b => b.Phone).HasMaxLength(32);
        builder.Property(b => b.TimeZone).HasMaxLength(64);

        builder.HasOne<Branch>().WithMany().HasForeignKey(b => b.ParentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(b => new { b.TenantId, b.Code }).IsUnique().HasDatabaseName(CodeIndexName);
        // Subtree queries: path LIKE '/a/b/%' within a tenant (text_pattern_ops makes LIKE-prefix index-friendly).
        builder.HasIndex(b => new { b.TenantId, b.Path }).HasOperators("uuid_ops", "text_pattern_ops");
        builder.HasIndex(b => b.ParentId);
    }
}

public class TenantMembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public const string TenantUserIndexName = "ux_tenant_memberships_tenant_user";

    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Stamp).IsRequired().HasMaxLength(32);
        builder.Property(m => m.RoleIds).HasColumnType("uuid[]");
        builder.Property(m => m.Branches)
            .HasColumnType("jsonb")
            .HasConversion(JsonList<BranchAssignment>.Converter, JsonList<BranchAssignment>.Comparer);

        builder.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique().HasDatabaseName(TenantUserIndexName);
        builder.HasIndex(m => m.UserId); // "my tenants" across tenants (host scope)
    }
}

public class TenantRoleConfiguration : IEntityTypeConfiguration<TenantRole>
{
    public const string NameIndexName = "ux_tenant_roles_tenant_name";

    public void Configure(EntityTypeBuilder<TenantRole> builder)
    {
        builder.ToTable("tenant_roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(TenantRole.NameMaxLength);
        builder.Property(r => r.Description).HasMaxLength(TenantRole.DescriptionMaxLength);
        builder.Property(r => r.SystemKey).HasMaxLength(32);
        builder.Property(r => r.Permissions).HasColumnType("text[]");
        builder.Ignore(r => r.EffectivePermissions);

        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique().HasDatabaseName(NameIndexName);
    }
}

public class TenantInvitationConfiguration : IEntityTypeConfiguration<TenantInvitation>
{
    public void Configure(EntityTypeBuilder<TenantInvitation> builder)
    {
        builder.ToTable("tenant_invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).IsRequired().HasMaxLength(TenantInvitation.EmailMaxLength);
        builder.Property(i => i.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(i => i.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.RoleIds).HasColumnType("uuid[]");
        builder.Property(i => i.Branches)
            .HasColumnType("jsonb")
            .HasConversion(JsonList<BranchAssignment>.Converter, JsonList<BranchAssignment>.Comparer);

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.Email, i.Status });
    }
}

public class TenantAuditLogConfiguration : IEntityTypeConfiguration<TenantAuditLog>
{
    public void Configure(EntityTypeBuilder<TenantAuditLog> builder)
    {
        builder.ToTable("tenant_audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(TenantAuditLog.ActionMaxLength);
        builder.Property(a => a.TargetType).IsRequired().HasMaxLength(64);
        builder.Property(a => a.TargetId).HasMaxLength(64);
        builder.Property(a => a.DataJson).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.CorrelationId).HasMaxLength(128);

        builder.HasIndex(a => new { a.TenantId, a.OccurredAt });
    }
}

/// <summary>jsonb mapping for small value lists (records) with a deep comparer for change tracking.</summary>
internal static class JsonList<T>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<List<T>, string> Converter = new(
        v => JsonSerializer.Serialize(v, Options),
        v => JsonSerializer.Deserialize<List<T>>(v, Options) ?? new List<T>());

    public static readonly ValueComparer<List<T>> Comparer = new(
        (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
        v => JsonSerializer.Serialize(v, Options).GetHashCode(),
        v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, Options), Options) ?? new List<T>());
}
