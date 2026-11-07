using System.Collections.Immutable;
using Identity.Domains.Audit.Models;
using Identity.Domains.Onboarding.Models;
using Identity.Domains.Sessions.Models;
using Identity.Domains.Sso.Models;
using Shared.Core.Auditing;
using Identity.Infrastructure.DataProtection;
using Identity.Models;
using Shared.Core.Messaging.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Identity.Data;

/// <summary>
/// Identity DbContext with ASP.NET Core Identity
/// Inherits from IdentityDbContext to get all Identity tables
/// </summary>
public class ApplicationIdentityDbContext : IdentityDbContext<
    ApplicationUser,
    ApplicationRole,
    Guid,
    IdentityUserClaim<Guid>,
    IdentityUserRole<Guid>,
    IdentityUserLogin<Guid>,
    IdentityRoleClaim<Guid>,
    IdentityUserToken<Guid>,
    IdentityUserPasskey<Guid>>, IDbContext
{
    public ApplicationIdentityDbContext(DbContextOptions<ApplicationIdentityDbContext> options)
        : base(options)
    {
    }

    // Additional DbSets
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<OnboardingProcess> OnboardingProcesses => Set<OnboardingProcess>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<AdminActionAuditLog> AdminAuditLogs => Set<AdminActionAuditLog>();
    public DbSet<AccountDeletion> AccountDeletions => Set<AccountDeletion>();
    public DbSet<SessionRevocation> SessionRevocations => Set<SessionRevocation>();
    public DbSet<DataProtectionKeyEntry> DataProtectionKeys => Set<DataProtectionKeyEntry>();
    public DbSet<SsoConnection> SsoConnections => Set<SsoConnection>();
    public DbSet<SsoDomain> SsoDomains => Set<SsoDomain>();
    public DbSet<Domains.Scim.Models.ScimDirectory> ScimDirectories => Set<Domains.Scim.Models.ScimDirectory>();
    public DbSet<Domains.Scim.Models.ScimUser> ScimUsers => Set<Domains.Scim.Models.ScimUser>();
    public DbSet<Domains.Scim.Models.ScimGroup> ScimGroups => Set<Domains.Scim.Models.ScimGroup>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditLogIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAuditLogIsAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>The admin audit is append-only: inserting is the only allowed change.</summary>
    private void EnsureAuditLogIsAppendOnly()
    {
        if (ChangeTracker.Entries<AdminActionAuditLog>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Admin audit log entries are append-only: they cannot be changed or deleted.");
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Call base to configure Identity tables
        base.OnModelCreating(builder);

        // Set schema for Identity tables
        builder.HasDefaultSchema("identity");

        // Configure table names to snake_case
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("roles");
        });

        builder.Entity<IdentityUserRole<Guid>>(entity =>
        {
            entity.ToTable("user_roles");
        });

        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("user_claims");
        });

        builder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("user_logins");

            // Schema v3 would cap the key at 128; enterprise SSO stores the IdP subject here (OIDC sub up to 255,
            // long SAML persistent NameIDs), so keep room for it.
            entity.Property(l => l.ProviderKey).HasMaxLength(256);
        });

        builder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("role_claims");
        });

        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("user_tokens");
        });

        // WebAuthn credentials (ASP.NET Core Identity schema v3). Rows cascade with the user (account erasure).
        // Mapped explicitly (same as Identity's schema v3 mapping) so the model does not depend on
        // IdentityOptions.Stores.SchemaVersion being resolvable, e.g. design-time and architecture-test model builds.
        builder.Entity<IdentityUserPasskey<Guid>>(entity =>
        {
            entity.ToTable("user_passkeys");
            entity.HasKey(p => p.CredentialId);
            entity.Property(p => p.CredentialId).HasMaxLength(1024);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.UserId).IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            entity.OwnsOne(p => p.Data).ToJson();
        });

        // Apply configurations from assembly
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Transactional outbox (UserDeletedIntegrationEvent, UserSessionsRevokedIntegrationEvent).
        builder.AddOutbox();

        // Generic "who changed what" for IAuditable entities (SSO connections, SCIM directories).
        builder.AddAuditTrail();

        // Apply snake_case naming
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
    }

    public IReadOnlyList<IDomainEvent> GetDomainEvents()
    {
        var domainEntities = ChangeTracker
            .Entries<IAggregate>()
            .Where(x => x.Entity.DomainEvents.Any())
            .Select(x => x.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(x => x.DomainEvents)
            .ToImmutableList();

        domainEntities.ForEach(entity => entity.ClearDomainEvents());

        return domainEvents;
    }
}
