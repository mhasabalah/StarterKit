using Microsoft.Extensions.Configuration;

namespace Tenancy.Data.Seed;

/// <summary>
/// Idempotent control-plane seed (runs at startup, host scope):
/// <list type="bullet">
/// <item>the Default tenant (<see cref="TenantDefaults.DefaultTenantId"/>) - always present, so single-tenant data and
/// later multi-tenant data share one model;</item>
/// <item>its head-office branch;</item>
/// <item>every system tenant role for EVERY tenant (new system roles reach existing tenants on deploy).</item>
/// </list>
/// </summary>
public sealed class TenancyDataSeeder(TenancyDbContext dbContext, IConfiguration configuration) : IDataSeeder
{
    public static readonly Guid DefaultHeadOfficeId = new("70000000-0000-0000-0000-000000000001");

    public Type DbContextType => typeof(TenancyDbContext);

    public async Task SeedAllAsync()
    {
        using (TenantScope.BeginHostScope())
        {
            if (!await dbContext.Tenants.AnyAsync(t => t.Id == TenantDefaults.DefaultTenantId))
            {
                var tenant = Tenant.Create(TenantDefaults.DefaultIdentifier,
                    configuration["AppOptions:Name"] is { Length: > 0 } name ? name : "Default",
                    Guid.Empty, TenantIsolation.Shared, id: TenantDefaults.DefaultTenantId);
                tenant.ClearDomainEvents(); // no provisioning: the shared schema is migrated/seeded at startup
                dbContext.Tenants.Add(tenant);
            }

            if (!await dbContext.Branches.AnyAsync(b => b.TenantId == TenantDefaults.DefaultTenantId))
            {
                var headOffice = Branch.Create("HQ", "Head Office", null, int.MaxValue, isHeadOffice: true,
                    id: DefaultHeadOfficeId);
                headOffice.TenantId = TenantDefaults.DefaultTenantId;
                dbContext.Branches.Add(headOffice);
            }

            await dbContext.SaveChangesAsync();

            var tenantIds = await dbContext.Tenants
                .Where(t => t.Status != TenantStatus.Deleted)
                .Select(t => t.Id)
                .ToListAsync();
            var existing = (await dbContext.Roles
                    .Where(r => r.SystemKey != null)
                    .Select(r => new { r.TenantId, r.SystemKey })
                    .ToListAsync())
                .Select(r => (r.TenantId, r.SystemKey!))
                .ToHashSet();

            foreach (var tenantId in tenantIds)
                foreach (var definition in SystemTenantRoles.All.Where(d => !existing.Contains((tenantId, d.Key))))
                {
                    dbContext.Roles.Add(TenantRole.CreateSystem(tenantId, definition));
                }

            await dbContext.SaveChangesAsync();
        }
    }
}
