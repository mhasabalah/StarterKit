# Isolation and Dedicated Databases

## Control plane vs data plane

| Plane | Contexts | Database |
|---|---|---|
| **Control plane** | `ApplicationIdentityDbContext`, `TenancyDbContext`, `SubscriptionsDbContext`, `NotificationDbContext`, Hangfire | Always the **host** (shared) database |
| **Data plane** | Every `ITenantDataContext` - `CatalogDbContext`, `AttachmentDbContext`, and new business modules | The shared database **or** the tenant's dedicated database |

Users are global (identity in the host DB), billing is platform-level (the tenant is the subscriber), so only business
data moves into a dedicated database.

## The three isolation layers

1. **Resolution** - the tenant of a request comes from the token (`tid`), or from host/header validated against it.
2. **EF Core** - every `IMustHaveTenant` entity gets the **named** query filter `Tenant`
   (`TenantFilterDisabled || e.TenantId == CurrentTenantId`). Filters are named (EF Core 10), so
   `IgnoreQueryFilters([QueryFilterNames.SoftDelete])` / `IgnoreNonIsolationFilters()` never drop tenant isolation.
   `TenantEntityInterceptor` stamps `TenantId` on insert and blocks inserting into, updating, deleting or re-parenting
   another tenant's rows (`CrossTenantAccessException` → 404, logged as a security event).
3. **PostgreSQL Row-Level Security** - see below.

**Fail closed**: an unresolved tenant (`CurrentTenantId = null`) matches no rows, RLS returns nothing, inserts throw
`TenantNotResolvedException` (400).

**Host scope** (`TenantScope.BeginHostScope()`): the only way to bypass layers 2-3 - used by migrations, seeding,
the tenant store/membership resolver, platform admin queries and the purge. Keep such blocks small and explicit.

## Row-Level Security

Migrations that add `tenant_id` also call `migrationBuilder.EnableTenantRowLevelSecurity(schema, table)`:

```sql
ALTER TABLE "catalog"."products" ENABLE ROW LEVEL SECURITY;
ALTER TABLE "catalog"."products" FORCE ROW LEVEL SECURITY;      -- applies to the table owner too
CREATE POLICY tenant_isolation ON "catalog"."products"
  USING      (current_setting('app.bypass_rls', true) = 'on'
              OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  WITH CHECK (same);
```

`TenantSessionConnectionInterceptor` sets `app.tenant_id` and `app.bypass_rls` on **every** connection open of a
tenant-data context (pooled connections can never carry a previous tenant).

> **Superusers bypass RLS.** The default Docker/Aspire setup connects as the `postgres` superuser, where RLS is inert
> (layers 1-2 still protect you). For production create a dedicated role:
>
> ```sql
> CREATE ROLE app LOGIN PASSWORD 'CHANGE_ME' NOSUPERUSER NOBYPASSRLS CREATEDB;  -- CREATEDB only for dedicated provisioning
> ALTER DATABASE starterkit OWNER TO app;  -- or GRANT the needed privileges per schema
> ```
>
> and use it in `ConnectionStrings:starterkit`. `MultiTenancy:Database:EnableRowLevelSecurity=false` keeps the
> policies installed but sets `app.bypass_rls=on` (inert).

## Shared vs dedicated

| | Shared (default) | Dedicated |
|---|---|---|
| Where | `tenant_id` rows in the shared DB | Own database (`{database}_t_{identifier}` on the shared server, or any server via `ConnectionSecretRef`) |
| Isolation | Filters + interceptor + RLS | Physical (filters/RLS still apply inside) |
| Provisioning | Instant (seeding in background) | `Provisioning` → DB created + migrated + seeded by `ProvisionTenantJob` → `Active` |
| Cost / ops | Lowest | One pool + one migration target per tenant |
| Typical use | SMB / self-service tenants | Enterprise, data residency, noisy tenants |

Choose per tenant at creation (platform admins: `isolation`, `connectionSecretRef`, `region` in `POST /tenants`).

### Connection resolution (`TenantConnectionStringResolver`)

* Shared tenant, host scope, unresolved tenant, or multi-tenancy disabled → the shared `ConnectionStrings:starterkit`.
* Dedicated with `ConnectionSecretRef = "acme-eu"` → `MultiTenancy:Connections:acme-eu` or `ConnectionStrings:acme-eu`
  (put it in a Docker secret / env var: `MultiTenancy__Connections__acme-eu`). **Never stored in the database.**
* Dedicated without a ref → shared server, database `MultiTenancy:Database:DedicatedDatabaseNameFormat`.
* `Maximum Pool Size` is capped at `MaxDedicatedPoolSize`.
* A dedicated tenant whose connection cannot be resolved **fails** (never falls back to the shared database).

### Migrations

* Shared DB: `UseMigration<TContext>()` at startup (host scope).
* Dedicated DBs: `DedicatedTenantDatabaseMigrator` (hosted service) migrates every active dedicated tenant in the
  background with `MigrationParallelism`; failures are logged per tenant. Disable with
  `MultiTenancy:Database:MigrateDedicatedOnStartup=false` and run `POST /host/tenants/{id}/provision` instead.

## Storage (attachments)

Every blob key is prefixed `t/{tenantId:N}/…` (`StoragePaths.TenantRoot`). Pre-multi-tenancy keys without the prefix
belong to the Default tenant. The orphan sweeper lists only the current tenant's prefix, and the tenant purge deletes
the whole prefix.

## Caches

Tenant data in caches must use `ITenantCacheKeys.For(key)` → `t:{tenantId}:{key}` (example: `CategoryTreeCache`).
Platform/user-keyed entries (token blacklist, plan catalog, subscription-by-subscriber) keep plain keys.
