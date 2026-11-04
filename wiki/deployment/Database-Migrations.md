# Database Migrations in Deployment

Production does **not** migrate from the API at boot. A one-shot run of the same image does it before any API
instance starts.

## Flow

1. `docker compose run --rm migrator` runs `Api.dll --migrate-only` (the compose `command` is `["--migrate-only"]`; the
   image ENTRYPOINT is `dotnet Api.dll`).
2. It migrates and seeds every module (Identity, Tenancy, Attachment, Catalog, Subscriptions, Notification, the same
   order as before), prepares the Hangfire schema, then migrates and seeds every **dedicated tenant database**.
   Exit code `0` = success; non-zero (failed migration, or any failed tenant database) = deploy stops, the old API keeps
   running.
3. `api` starts only after `migrator` completed successfully (`service_completed_successfully`).
   `deployment/deploy.sh` and the CI deploy job run the migrator explicitly first, then start every other service with
   `up -d --no-deps <explicit list without migrator>`, so the migrator runs **exactly once per deploy**. (On the very
   first deployment CI uses a plain `up -d`, where `depends_on` runs it once.)

## Configuration (`Database` section)

| Key | Default | Meaning |
|---|---|---|
| `MigrateOnStartup` | `true` (`false` in `appsettings.Docker.json`) | Migrate + seed when the app boots. Keep `true` for dev, Aspire and tests. |
| `MigrateOnly` | `false` | Set by the `--migrate-only` argument. Always migrates, then the process exits. |
| `MigrationConnectionString` | empty | Privileged connection used **only** to apply migrations of the shared database (and to install the Hangfire schema during `--migrate-only`). Env: `Database__MigrationConnectionString`, compose: the `migration_connection_string` Docker secret, i.e. `secrets/migration_connection_string.txt`, mounted as `Database__MigrationConnectionString`. |
| `LockTimeoutSeconds` | `300` | Wait for another migrator holding the lock before failing. |

`MultiTenancy:Database:MigrateDedicatedOnStartup` still controls the background migration of dedicated tenant
databases when `MigrateOnStartup=true`.

## Concurrency

Each migrate + seed runs under a Postgres **session advisory lock** (`pg_try_advisory_lock`, one key per database, on a
dedicated unpooled connection), so replicas booting together, or a migrator racing an app with `MigrateOnStartup=true`,
serialize; the loser finds nothing pending. The lock is released if the process dies. Dedicated tenant databases lock
their own database.

## Least-privilege roles (optional, recommended)

Create the roles yourself (the `deployment/postgres` init scripts are not part of this flow):

```sql
CREATE ROLE starterkit_migrator LOGIN PASSWORD '...';   -- owns the schemas, runs DDL
CREATE ROLE starterkit_app LOGIN PASSWORD '...';        -- runtime: DML only, NOT superuser, NOT BYPASSRLS
-- after the first migrator run, for each schema (identity, tenancy, catalog, attachment, subscriptions, notification, hangfire, transport):
GRANT USAGE ON SCHEMA <s> TO starterkit_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA <s> TO starterkit_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA <s> TO starterkit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE starterkit_migrator IN SCHEMA <s> GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO starterkit_app;
ALTER DEFAULT PRIVILEGES FOR ROLE starterkit_migrator IN SCHEMA <s> GRANT USAGE, SELECT ON SEQUENCES TO starterkit_app;
```

`hangfire` (Hangfire.PostgreSql, `HangfireConfiguration`) and `transport` (`Messaging:PostgreSql:Schema`, MassTransit SQL
transport) are created by the migration role like the module schemas, so they need the same grants. The same loop plus
the default privileges is shipped in `deployment/postgres/scripts/init-rls-roles.sql`.

**New schema checklist.** `ALTER DEFAULT PRIVILEGES` only applies to schemas that already exist, and `GRANT ... ON ALL
TABLES` only to objects that exist when it runs. When a migration creates a NEW schema (a new module, or a first
`transport`/`hangfire` install), re-run the grant script (or the SQL above for that schema) **before the deploy that
ships it reaches the API**, otherwise the runtime role gets `permission denied` on the new schema. New tables in an
existing schema are covered by the default privileges, as long as the migration role is the one that creates them.

**Seeding uses the runtime connection.** The migrator applies DDL with `MigrationConnectionString`, but the seeding
step that follows runs through the normal module contexts, i.e. with the runtime connection (DML only). Seeds therefore
only need DML grants, and on tenant-data tables they run in host scope (with `RlsBypass=Role` that means the host role
connection).

Point `POSTGRES_USER`/secret at `starterkit_app` and set `MIGRATION_CONNECTION_STRING` to the migrator role. The advisory
lock uses the migrator connection. With `Messaging:Transport=PostgreSql`, the transport schema is created by MassTransit
at API start using `Messaging:PostgreSql:AdminUsername/AdminPassword` (set them to the migrator role, or set
`CreateInfrastructure=false` after the first run). Dedicated tenant databases use their own per-tenant credentials.

## Other deploy-time guards

- `Messaging:Transport=InMemory` (or unset) aborts startup outside Development/Testing/Test/E2E.
- Request timeouts: `RequestTimeouts:DefaultSeconds` (30) for all endpoints, `LongRunningSeconds` (300) for attachment
  upload/download; the Hangfire dashboard is exempt. Timeouts answer `504`. Keep Nginx `proxy_read_timeout` above them.
- Health: `/health` is readiness (compose healthcheck, deploy gate), `/alive` is liveness.
- CI: the deploy job fails immediately when `APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB` or
  `BACKUP_BUCKET` repository variables are missing.
