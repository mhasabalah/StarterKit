# Database conventions

These are the EF Core 10 and PostgreSQL 16 conventions. For migrations, see [../Reference/Migrations.md](../Reference/Migrations.md)
and `/starterkit:add-migration`. For new entities, see `/starterkit:add-aggregate`. For tenant data, see
[../MultiTenancy/Adding-a-Tenant-Aware-Module.md](../MultiTenancy/Adding-a-Tenant-Aware-Module.md).

## One database, one schema per module

- **One database.** All modules share one database (`ConnectionStrings:starterkit`), and each module owns **its
  own schema and DbContext**: `identity`, `tenancy`, `attachment`, `catalog`, `subscriptions`, `notification`.
  - Each module keeps its own migrations history table (`UseModuleMigrationsHistory<TContext>()`).
  - Each module auto-migrates on startup (`UseMigration<TContext>()`).
- **No cross-schema foreign keys and no cross-module joins.** A module stores another module's id as a plain `Guid`
  (for example `OwnerId`) and reads its data through `ISender` + `.Contracts`. That keeps modules extractable.
  [ADR-0001](../Decisions/0001-modular-monolith-schema-per-module.md) explains why.

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);
    builder.HasDefaultSchema("catalog");
    builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    builder.FilterSoftDeletedProperties();       // only if the module soft-deletes
    builder.AddTenantOutbox();                   // only if it publishes integration events (AddOutbox() for host contexts)
    builder.ToSnakeCaseTables();
    builder.ConfigureVersionConcurrencyTokens();
    ApplyTenantFilters(builder);                 // tenant-data contexts: LAST
}
```

The context is registered with `services.AddCustomDbContext<TContext>(configuration, "starterkit")`. That call
configures Npgsql, snake_case naming and the interceptors: auditing and versioning, domain-event collection, tenant
stamping and the RLS session.

## Naming

- snake_case everywhere, through `EFCore.NamingConventions`: table `product_images`, column `published_at`, index
  `ix_products_tenant_id_branch_id`.
- Name unique indexes explicitly and keep the name in a constant, so the repository can translate violations:
  `HasDatabaseName(ProductConfiguration.SkuIndexName)` then `ex.IsUniqueViolation(ProductConfiguration.SkuIndexName)`.
- Owned `LocalizedText` maps to `name_en` / `name_ar` columns.

## Keys, types and columns

| Concern | Rule |
|---|---|
| Primary key | `Guid Id`, generated in the factory (`Guid.NewGuid()`) |
| Enums | `.HasConversion<string>().HasMaxLength(n)`: readable and safe to reorder |
| Strings | Always `HasMaxLength` (backed by a `const` on the entity) |
| Money | `decimal` with an explicit precision + currency column |
| Booleans | `IsRequired()`; no `HasDefaultValue` (EF sentinel problems) |
| Concurrency | `long Version` from `Entity<T>`; configured by `ConfigureVersionConcurrencyTokens()`; never set it by hand |
| Audit columns | `CreatedAt`/`CreatedBy`/`LastModified`/`LastModifiedBy`, set by `AuditableEntityInterceptor` |
| Tenant | `tenant_id` on every tenant-data table; stamped by `TenantEntityInterceptor` |

### Timestamps (UTC only)

Npgsql runs in its default (non-legacy) mode, so `DateTime` and `DateTimeOffset` map to `timestamptz`.
- Write `DateTime` values with `Kind=Utc` (`TimeProvider.GetUtcNow().UtcDateTime`, `DateTime.UtcNow`). `Kind=Local`
  or `Unspecified` throws on save.
- Normalize client `DateTimeOffset` input with `ToUniversalTime()`.
- Never turn on `Npgsql.EnableLegacyTimestampBehavior`.

## Relationships

Relationships exist **inside an aggregate** (root → children), with an explicit delete behaviour:

```csharp
builder.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
builder.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);   // private List<T> backing field
```

Between aggregates of the same module, reference by id, with an FK only where integrity matters (for example
`Product.CategoryId → categories`, `OnDelete(Restrict)`). Between modules, never use an FK.

## Indexes

- Index what you filter and sort on: foreign keys, status, `(is_deleted, deleted_at)` for purge jobs.
- Tenant-data unique indexes **start with `tenant_id`**: `HasIndex(x => new { x.TenantId, x.Sku }).IsUnique()`.
  `TenantIsolationArchitectureTests` enforces this.
- An owned-column unique index (`name_en`) cannot include `tenant_id` in EF. Create it with raw SQL in the
  migration, using the same name as the constant.
- Use filtered unique indexes (`HasFilter("is_deleted = false")`) when soft-deleted rows must not block reuse.

## Query filters

All filters are **named** (EF Core 10, `QueryFilterNames`):
- `SoftDelete` from `FilterSoftDeletedProperties()`
- `Tenant` and `Branch` from `ApplyTenantFilters`
- module-specific filters such as `Lifecycle`

```csharp
// Admin/restore/purge paths: drop soft-delete & lifecycle filters, KEEP tenant isolation.
var deleted = await context.Products.IgnoreNonIsolationFilters().Where(p => p.IsDeleted).ToListAsync(ct);
// Branch-wide view for users with tenant.branches.all:
var all = await context.Products.IgnoreBranchScope().ToListAsync(ct);
```

The parameterless `IgnoreQueryFilters()` is forbidden in production code; an architecture test checks this.

## Reading data

- Query handlers use `AsNoTracking()` and **project in SQL** (`Select(x => new Dto(...))`), then page with
  `.PagedResult(pageNumber, pageSize, ct)` (`Shared.Core/Extensions/QueryExtensions.cs`).
- Repositories load aggregates with their children (`Include(...)`, `AsSplitQuery()` for multiple collections).
  Don't lazy load.
- Avoid N+1: no per-row queries inside loops. Batch with `Where(x => ids.Contains(x.Id))`.
- `LIKE` search escapes user input (see `Catalog/Common/LikePattern.cs`) and uses `EF.Functions.ILike`.

## Writing data and transactions

- One command = one `SaveChangesAsync`, which is transactional by itself. Domain events are collected on save and
  published only after a successful commit.
- If you need several saves to commit together, use an explicit transaction
  (`await using var tx = await db.Database.BeginTransactionAsync(ct)`). Events of an uncommitted or rolled-back
  transaction are discarded.
- Concurrency conflicts surface as `DbUpdateConcurrencyException`, which becomes 409 through
  `ConcurrencyHandlingBehavior`. For details, see `CLAUDE.md` → *Optimistic concurrency*.
- Postgres unique violations (23505) are translated to `ConflictException` in the repository with
  `IsUniqueViolation`.

## Seeding

Implement `IDataSeeder` (`SeedAllAsync`, `DbContextType`) and register it in `<M>Module.cs`, as Catalog's
`CatalogDataSeeder` does.
- Seeding is idempotent: check before inserting.
- Use fixed ids per tenant from `TenantSeedIds.For(tenantId, seedId)`.
- Seeders run after migrations (`UseMigration<T>()`). Seeders of tenant-data contexts run as the Default tenant.
  Dedicated tenant databases are migrated and seeded by Tenancy provisioning (`MigrateAndSeedAsync`).

## Row-level security

Tenant-data tables get Postgres RLS as a second isolation layer under the EF filter:
`migrationBuilder.EnableTenantRowLevelSecurity(schema, table)` for every table, including `outbox_messages`. The
connection interceptor sets the session's tenant. See
[../MultiTenancy/Isolation-and-Dedicated-Databases.md](../MultiTenancy/Isolation-and-Dedicated-Databases.md).

Related: [ApiGuidelines.md](ApiGuidelines.md) · [../Testing/Testing-Strategy.md](../Testing/Testing-Strategy.md)
