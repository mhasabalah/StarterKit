# Deploy database migrations

Run the published application's one-shot migrator before replacing runtime instances, covering shared schemas and dedicated tenant databases.

## Goal

Apply migrations and seeds under PostgreSQL advisory locks while keeping runtime database privileges separate from DDL privileges.

## Prerequisites

- A built API image and reachable PostgreSQL with the required migration, runtime and host-scope credentials.
- A verified backup and a compatible release plan; an image rollback does not reverse schema changes.
- Module migrations created with `/starterkit:add-migration`.
- Existing module-schema, concurrency and RLS conventions from [Database conventions](../Architecture/DatabaseConventions.md).
- The `vector` extension (pgvector) available on the server: the Agentic migration runs `CREATE EXTENSION IF NOT
  EXISTS vector`. The repository's `infra/postgres` image includes it; on a managed service, allow-list it. When the
  migration role may not create extensions, run `deployment/postgres/scripts/init-rls-roles.sql` (it creates the
  extension) or `CREATE EXTENSION vector;` as an administrator in every database first, dedicated tenant databases
  included ([ADR-0028](../Decisions/0028-agent-knowledge-and-memory-on-pgvector.md)).

## Steps

1. Keep `Database:MigrateOnStartup=false` for runtime instances. This is set in `src/Api/appsettings.Docker.json`;
   development/tests default to true. Review the resolved runtime environment rather than assuming the setting
   applies to every custom production host.

   | Key | Default (`DatabaseMigrationOptions`) | Meaning |
   |---|---|---|
   | `Database:MigrateOnStartup` | `true`; Docker `false` | Normal module startup migrates and seeds |
   | `Database:MigrateOnly` | `false` | CLI `--migrate-only` sets it and forces migration, then exits |
   | `Database:MigrationConnectionString` | `null` | Optional privileged shared-database connection |
   | `Database:LockTimeoutSeconds` | `300` | Advisory-lock acquisition timeout |

2. If separating privileges, provision a DDL owner/migrator and a DML runtime user. Set the runtime connection
   through the normal database settings and place the privileged connection in
   `secrets/migration_connection_string.txt`. Compose mounts it only in migrator as
   `/run/secrets/Database__MigrationConnectionString`; the provider converts `__` to `:`. An empty file falls
   back to the runtime connection. The configuration key can also be supplied as `Database__MigrationConnectionString`
   outside Compose; there is no base-file `MIGRATION_CONNECTION_STRING` interpolation.

3. Configure role-based tenant RLS before serving traffic. Run the reviewed
   `deployment/postgres/scripts/init-rls-roles.sql` as a cluster administrator; it is not automatically invoked
   by the base Compose init mount (`./postgres/init`). Use `CHANGE_ME` only as documentation placeholders.

   - `starterkit_app`: non-superuser, no `BYPASSRLS`, not a table owner or member of `tenant_rls_bypass`.
   - `starterkit_host`: host-scope login with `BYPASSRLS` and required DML privileges.
   - `tenant_rls_bypass`: `NOLOGIN` group whose existence hardens the policy helper against arbitrary
     `app.bypass_rls` flags. The runtime user must not belong to it.
   - Set `MultiTenancy:Database:RlsBypass=Role` and `MultiTenancy:Database:HostConnectionString` for host work.
     Do not route tenant-data connections through transaction pooling.

   The grant script assumes migration owner `starterkit_migrator` for default privileges. Review its schema
   list: it does not currently include `webhooks` or `datatransfer`. Grant usage, table DML and sequence usage
   to runtime/host roles for every deployed schema, including `hangfire` and PostgreSQL messaging `transport`.
   Reapply grants when adding a new schema; defaults apply to future objects only in existing schemas and only
   for the configured creating role. Seeding uses normal module contexts after shared DDL; shared tenant-data
   seeders run as the Default tenant, and dedicated seeders run as their tenant. DML credentials must work for
   those scopes before migration is considered complete; migration host scopes also require the host credential.

4. Run the migration once before an existing API replacement. On a server upgraded from an image without pgvector,
   rebuild and recreate `postgres` first (CI and `deployment/deploy.sh` do it), or the Agentic migration fails with
   "could not open extension control file":

   ```bash
   docker compose -f docker-compose.yml --env-file .env.prod build postgres
   docker compose -f docker-compose.yml --env-file .env.prod up -d --no-deps --wait postgres
   docker compose -f docker-compose.yml --env-file .env.prod run --rm migrator
   ```

   For source-based development, the equivalent entry point is:

   ```bash
   dotnet run --project src/Api -- --migrate-only
   ```

   `src/Api/Program.cs` initializes discovered modules in `ModuleOrder`, migrates/seeds shared contexts,
   provisions the PostgreSQL transport when selected, and calls `DedicatedTenantDatabaseMigrator.MigrateAllAsync`.
   Hangfire prepares its schema only when migration is enabled; the one-shot mode uses the migration connection
   when supplied. The bus hosted service does not run in migrate-only mode; transport DDL is explicitly invoked.

5. Require exit code zero, then start the API. Base Compose's `service_completed_successfully` dependency handles
   first startup; CI runs the migrator explicitly on later releases before `up -d --no-deps api`. Supporting-service
   updates exclude migrator so an exited one-shot service is not started twice.

## Verify

Inspect migrator logs and the exit code. A failed dedicated tenant makes the one-shot run fail; do not start
the new application against a partially migrated fleet. After replacement check `/health`, not only `/alive`.
Run `PendingModelChangesTests` and `TenantRowLevelSecurityCoverageTests` against the real migrations during CI.
With role mode, verify both runtime isolation and authorized host work; an unreachable role-check database is
logged rather than treated as proof of valid roles.

## Pitfalls

- `MigrationLock` uses `pg_try_advisory_lock` on a dedicated unpooled session, with one shared key per database.
  Competing migrators serialize; timeout fails the run. Disposal or connection loss releases the lock. Each
  dedicated database acquires its own lock; this is not one transaction across all module/tenant databases.
- `MigrationConnectionString` overrides shared DDL and one-shot Hangfire setup; it does not replace per-tenant
  connection strings. Dedicated migrations resolve the tenant's own database and credentials.
- PostgreSQL transport provisioning obeys `Messaging:PostgreSql:CreateInfrastructure` (default `true`). Explicit
  `Messaging:PostgreSql:AdminUsername` / `AdminPassword` override the chosen admin credentials; otherwise the
  one-shot migrator derives them from `Database:MigrationConnectionString` when supplied. Setting
  `CreateInfrastructure=false` means an administrator must already have installed the transport infrastructure.
  Runtime instances with startup migration disabled do not create the transport schema/role at API boot.
- `MultiTenancy:Database:MigrateDedicatedOnStartup` concerns normal startup migration; the explicit one-shot run
  migrates dedicated tenants itself. See [dedicated database operations](../MultiTenancy/Isolation-and-Dedicated-Databases.md).
- `RlsBypassRoleCheck` fails for a reachable database missing the group or an unsafe runtime role when its role-mode
  prerequisites apply. It logs connection failures; readiness and deployment verification must still reject outages.
- Non-development production guards reject non-durable `InMemory` transport and unsafe placeholders. Request
  timeouts (`RequestTimeouts:DefaultSeconds=30`, `LongRunningSeconds=300`) are independent of migration locks;
  Hangfire dashboard is exempt. Review Nginx timeouts for long attachment operations.
- The deploy job requires explicit repository variables even though its environment expressions have fallbacks.

## Related

[Database conventions](../Architecture/DatabaseConventions.md), [CI/CD](CI-CD.md), [backups](Backups.md),
[migration creation](../Reference/Migrations.md), [ADR-0022](../Decisions/0022-production-message-transport.md),
and `/starterkit:add-migration`.
