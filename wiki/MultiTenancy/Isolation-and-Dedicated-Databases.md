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

### Hardened bypass: role instead of session variable

By default (`MultiTenancy:Database:RlsBypass=SessionVariable`) host scope sets `app.bypass_rls=on`. Since the runtime
role can set that variable itself, any raw SQL executed in a session could switch RLS off. The opt-in hardened mode
removes that:

* The policies call a per-schema helper `<schema>.tenant_rls_bypass()` (created by `EnableTenantRowLevelSecurity`;
  migrations `RlsBypassRoleHardening` in Catalog and Attachment upgraded existing tables). It returns true only when
  `app.bypass_rls='on'` **and** (the group role `tenant_rls_bypass` does not exist **or** `current_user` is a member).
  With no such role (default) behavior is unchanged; creating the role turns the hardening on cluster-wide for the
  databases that carry the helper. Justification: a policy cannot read app configuration, and role membership cannot be
  forged by a session, so the role's existence is the single DB-side switch and no session setting can lift RLS.
* `RlsBypass=Role` + `HostConnectionString` (a `BYPASSRLS` login role): `TenantSessionConnectionInterceptor` swaps the
  username/password to the host role in `ConnectionOpening` when the scope is host scope (server and database stay those
  of the context, so dedicated databases on the same server work; the host role must exist there too). The variable is
  never set to `on` by the app. `TenantDataMover` benefits automatically (it runs in host scope). Options validation
  requires RLS enabled and a `HostConnectionString`.
* Setup: `deployment/postgres/scripts/init-rls-roles.sql` creates `starterkit_app` (NOBYPASSRLS, not owner),
  `starterkit_host` (BYPASSRLS) and the `tenant_rls_bypass` group role (do not make the runtime role a member). Run
  migrations and seeding with an owner/migration role that is a member of `tenant_rls_bypass` or the host role.
* **Cluster-wide switch.** The hardened mode turns on whenever the `tenant_rls_bypass` role exists anywhere in the
  Postgres **cluster** (roles are cluster-level, not per database). Do not share one cluster between a hardened
  (`RlsBypass=Role`) deployment and a `SessionVariable` deployment: the latter would lose host scope everywhere (it
  fails closed). Give each mode its own cluster.
* **Startup check.** With `RlsBypass=Role` the app also verifies at startup that the runtime role is not a superuser, does
  not have `BYPASSRLS` and is not a member of `tenant_rls_bypass`, and refuses to start otherwise.
* Caveat: if config says `Role` but the `tenant_rls_bypass` role was never created, the runtime role can still bypass;
  verify with `SELECT 1 FROM pg_roles WHERE rolname='tenant_rls_bypass'`. The reverse (role exists, config
  `SessionVariable`) fails closed: host scope sees nothing.

### Connection pooling (PgBouncer)

RLS relies on **session-level** `set_config` values applied on every connection open. Behind a **transaction-pooling**
proxy (PgBouncer `pool_mode=transaction`) the value and the query can run on different server connections, so tenant
data could be seen or hidden wrongly. Use direct connections or **session** pooling for every tenant-data connection
string (also dedicated tenants and the host connection string). At startup `PoolingSafetyCheck` logs a warning for
connection strings using port 6432 or a host containing `pgbouncer` (heuristic only; the pool mode is invisible to the
client). Npgsql's own client-side pool is fine.

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

### In-process (L1) tenant caches across instances

`TenantStore` and `TenantMembershipResolver` keep an `IMemoryCache` L1 over Redis (L2). An invalidation evicts L1+L2 on
the instance that made the change and **broadcasts the evicted L1 keys** over Redis pub/sub
(`MultiTenancy:Cache:InvalidationChannel`, default `starterkit:tenancy:invalidate`; `BroadcastInvalidation=false`
disables). Every instance (including the sender) evicts the keys on receipt, which is idempotent. A suspended tenant is
therefore refused on all replicas immediately instead of after `Cache:LocalSeconds`. Without Redis the bus is a no-op;
a publish/subscribe failure is logged and never fails a request, and the L1 TTL stays the upper bound. Other modules
with L1 caches can inject `ILocalCacheInvalidationBus`.
