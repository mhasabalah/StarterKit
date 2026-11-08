# Adding a Tenant-Aware Module (checklist)

Business modules are **data plane**: their data belongs to tenants. Follow the Catalog sample.

## DbContext

```csharp
public class OrdersDbContext(DbContextOptions<OrdersDbContext> options)
    : AppDbContextBase(options), ITenantDataContext          // ← data plane marker
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("orders");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.FilterSoftDeletedProperties();                // named "SoftDelete" (only if you need soft delete)
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
        ApplyTenantFilters(builder);                          // ← LAST: Tenant/Branch filters + tenant_id indexes
    }
}
```

`AddCustomDbContext<OrdersDbContext>(configuration, "starterkit")` does the rest: per-tenant connection (shared or
dedicated), interceptors, RLS session variables, registration for provisioning/migration/purge.

## Entities

* Implement `IMustHaveTenant` (`public Guid TenantId { get; set; }`) on **every** table, children included (RLS needs
  `tenant_id` on each table). Never set it yourself - the interceptor stamps it.
* Branch-owned data: also `IBranchScoped` (`Guid? BranchId`).
* A genuinely global table inside a tenant-data context: `[HostEntity("reason")]`.
* Unique indexes **include `TenantId` first**: `HasIndex(x => new { x.TenantId, x.Code }).IsUnique()`.
* Only **named** query filters: `HasQueryFilter(QueryFilterNames.Lifecycle, …)`.

## Migration

```bash
dotnet ef migrations add InitialOrders --context OrdersDbContext --project src/Modules/Orders/Orders \
  --startup-project src/Api --output-dir Data/Migrations
```

Then, in the migration's `Up`, enable RLS for every table:

```csharp
migrationBuilder.EnableTenantRowLevelSecurity("orders", "orders");
migrationBuilder.EnableTenantRowLevelSecurity("orders", "order_lines");
```

(When adding `tenant_id` to existing tables: default the new column to `TenantDefaults.DefaultTenantId`, then
`DROP DEFAULT` - see `Catalog/Data/Migrations/*_AddMultiTenancy.cs`.)

## Queries

* Never call the parameterless `IgnoreQueryFilters()` (architecture test) - use `IgnoreNonIsolationFilters()`,
  `IgnoreBranchScope()` or name the filters.
* Cross-tenant/platform queries: wrap in `using (TenantScope.BeginHostScope()) { … }` and protect the endpoint with
  `.HostOnly().RequirePermission(Permissions.X)`.

## Endpoints

Endpoints declared in a module that owns an `ITenantDataContext` **require a tenant automatically**. Use
`.RequireTenantPermission(TenantPermissions.X)` / `.RequireTenantMember()` for tenant authorization (add new
permissions to `TenantPermissions` and to the system roles in `SystemTenantRoles` when appropriate).
`.TenantOptional()` / `.HostOnly()` opt out. Destructive/ownership endpoints that support staff must not trigger while
impersonating a user: `.DisallowImpersonation()`.

## Background work

* Recurring jobs on tenant data: `ITenantJobRunner.ForEachTenantScopeAsync(...)` (or `ForEachTenantAsync` for one
  unit of work per tenant).
* Jobs enqueued from a request automatically run as that request's tenant (Hangfire `TenantJobFilter`).
* MassTransit messages carry the tenant header; consumers run in that tenant.
* Domain event handlers run in the same flow → same tenant.

## Caches & files

* Cache tenant data under `ITenantCacheKeys.For("orders:…")`.
* Blob keys under `StoragePaths.TenantRoot(tenantId)`.

## Offboarding, export and moves

Rows are purged, exported (CSV) and moved between databases centrally - generically, for every table of an
`IMustHaveTenant` entity (which is why EVERY table needs `tenant_id`). If the module stores tenant data **outside** its DbContext (files, external APIs),
handle `TenantDataPurgeRequestedEvent` idempotently.

## Tests

The architecture tests (`Api.Tests/Architecture/TenantIsolationArchitectureTests`) pick the new context up
automatically: the module list is discovered from the host (ADR-0019).
