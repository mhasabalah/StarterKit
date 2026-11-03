using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Data;
using Shared.Core.Data.Seed;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Tenancy.Contracts.Branches;
using Tenancy.Contracts.Roles;
using Tenancy.Contracts.Tenants;
using Tenancy.Data;
using Tenancy.Data.Configurations;
using Tenancy.Data.Seed;
using Tenancy.Domains.Members.Models;
using Tenancy.Domains.Roles.Models;
using Tenancy.Domains.Tenants.Exceptions;
using Tenancy.Domains.Tenants.Models;

namespace Tenancy.IntegrationTests;

/// <summary>
/// Tenancy on real PostgreSQL with the real migrations: tenant creation as one transaction, seeding, and the
/// database-enforced uniqueness rules whose violations the handlers translate BY CONSTRAINT NAME (EF InMemory
/// cannot check any of this).
/// </summary>
[Collection(TenancyPostgresCollection.Name)]
public sealed class TenantLifecycleTests(TenancyPostgresFixture fx)
{
    private static string NewIdentifier() => $"t-{Guid.NewGuid():N}"[..20];

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenant(string? identifier = null)
    {
        var owner = Guid.NewGuid();
        var result = await fx.SendAs(owner, new CreateTenantCommand("Acme", identifier ?? NewIdentifier()));
        return (result.TenantId, owner);
    }

    // ── Creation & seeding ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTenant_persists_tenant_system_roles_head_office_and_owner_membership_atomically()
    {
        var (tenantId, owner) = await CreateTenant();

        var (roles, branches, membership) = await fx.WithDb(async db => (
            await db.Roles.Where(r => r.TenantId == tenantId).ToListAsync(),
            await db.Branches.Where(b => b.TenantId == tenantId).ToListAsync(),
            await db.Memberships.SingleAsync(m => m.TenantId == tenantId)));

        roles.Select(r => r.Id).Should().BeEquivalentTo(
            SystemTenantRoles.All.Select(d => SystemTenantRoles.IdOf(tenantId, d)), "system roles use per-tenant seed ids");
        branches.Should().ContainSingle(b => b.IsHeadOffice && b.Code == "HQ");
        membership.UserId.Should().Be(owner);
        membership.IsOwner.Should().BeTrue();
        membership.RoleIds.Should().Equal(SystemTenantRoles.IdOf(tenantId, SystemTenantRoles.Admin));
        (await fx.WithDb(db => db.AuditLogs.AnyAsync(a => a.TenantId == tenantId))).Should().BeTrue("creation is audited");
    }

    [Fact]
    public async Task CreateTenant_enqueues_provisioning_after_commit()
    {
        var (tenantId, _) = await CreateTenant();

        fx.Jobs.Verify(j => j.Create(
                It.Is<Job>(job => job.Args.Contains(tenantId)), It.IsAny<IState>()),
            Times.AtLeastOnce, "TenantCreatedEvent must enqueue provisioning for the committed tenant");
    }

    [Fact]
    public async Task Duplicate_identifier_is_rejected_and_nothing_else_is_written()
    {
        var identifier = NewIdentifier();
        await CreateTenant(identifier);

        var act = () => CreateTenant(identifier);

        await act.Should().ThrowAsync<TenantIdentifierAlreadyExistsException>();
        (await fx.WithDb(db => db.Tenants.CountAsync(t => t.Identifier == identifier))).Should().Be(1);
    }

    [Fact]
    public async Task Seeder_creates_the_Default_tenant_and_is_idempotent()
    {
        for (var run = 0; run < 2; run++)
        {
            await fx.WithServices(async sp =>
            {
                using (TenantScope.BeginHostScope())
                {
                    await sp.GetServices<IDataSeeder>().OfType<TenancyDataSeeder>().Single().SeedAllAsync();
                }
            });
        }

        (await fx.WithDb(db => db.Tenants.CountAsync(t => t.Id == TenantDefaults.DefaultTenantId))).Should().Be(1);
        (await fx.WithDb(db => db.Branches.CountAsync(b => b.Id == TenancyDataSeeder.DefaultHeadOfficeId))).Should().Be(1);
    }

    // ── Uniqueness enforced by the database, translated by constraint name ──────────────────────────

    [Fact]
    public async Task Branch_code_is_unique_per_tenant_not_globally()
    {
        var (tenantA, ownerA) = await CreateTenant();
        var (tenantB, ownerB) = await CreateTenant();

        await fx.SendInTenant(tenantA, ownerA, new CreateBranchCommand(null, "CAIRO", "Cairo"));
        var duplicate = () => fx.SendInTenant(tenantA, ownerA, new CreateBranchCommand(null, "CAIRO", "Cairo 2"));

        await duplicate.Should().ThrowAsync<BranchCodeAlreadyExistsException>(
            "the handler has no pre-check: only ux_branches_tenant_code catches it");
        await fx.SendInTenant(tenantB, ownerB, new CreateBranchCommand(null, "CAIRO", "Cairo"));
    }

    [Fact]
    public async Task Custom_role_name_is_unique_per_tenant()
    {
        var (tenantId, owner) = await CreateTenant();
        await fx.SendInTenant(tenantId, owner, new CreateTenantRoleCommand("Cashier", null, []));

        var duplicate = () => fx.SendInTenant(tenantId, owner, new CreateTenantRoleCommand("Cashier", null, []));

        await duplicate.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task A_custom_domain_can_belong_to_only_one_tenant()
    {
        var (tenantA, ownerA) = await CreateTenant();
        var (tenantB, ownerB) = await CreateTenant();
        var host = $"{Guid.NewGuid():N}.example.com";

        await fx.SendInTenant(tenantA, ownerA, new AddTenantDomainCommand(host));
        var stolen = () => fx.SendInTenant(tenantB, ownerB, new AddTenantDomainCommand(host));

        await stolen.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task A_user_has_at_most_one_membership_per_tenant()
    {
        var (tenantId, owner) = await CreateTenant();

        var act = () => fx.WithDb(async db =>
        {
            db.Memberships.Add(TenantMembership.Create(tenantId, owner, [], [], DateTime.UtcNow));
            return await db.SaveChangesAsync();
        });

        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.IsUniqueViolation(TenantMembershipConfiguration.TenantUserIndexName).Should().BeTrue();
    }

    [Fact]
    public async Task Every_translated_index_name_exists_in_the_migrated_schema()
    {
        string[] expected =
        [
            TenantConfiguration.IdentifierIndexName,
            TenantConfiguration.DomainHostIndexName,
            BranchConfiguration.CodeIndexName,
            TenantMembershipConfiguration.TenantUserIndexName,
            TenantRoleConfiguration.NameIndexName
        ];

        var indexes = await fx.WithDb(db => db.Database
            .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'tenancy'")
            .ToListAsync());

        indexes.Should().Contain(expected, "handlers map unique violations to 409 by these exact names");
    }
}
