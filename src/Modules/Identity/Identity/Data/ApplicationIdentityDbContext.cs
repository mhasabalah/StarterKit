using System.Collections.Immutable;
using Identity.Domains.Audit.Models;
using Identity.Domains.Onboarding.Models;
using Identity.Infrastructure.DataProtection;
using Identity.Models;
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
    IdentityUserToken<Guid>>, IDbContext
{
    public ApplicationIdentityDbContext(DbContextOptions<ApplicationIdentityDbContext> options)
        : base(options)
    {
    }

    // Additional DbSets
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OnboardingProcess> OnboardingProcesses => Set<OnboardingProcess>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<AdminActionAuditLog> AdminAuditLogs => Set<AdminActionAuditLog>();
    public DbSet<DataProtectionKeyEntry> DataProtectionKeys => Set<DataProtectionKeyEntry>();

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
        });

        builder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("role_claims");
        });

        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("user_tokens");
        });

        // Apply configurations from assembly
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

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

        return domainEvents.ToImmutableList();
    }
}
