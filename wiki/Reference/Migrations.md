# Database Migrations

Every module owns its own `DbContext`, its own PostgreSQL schema and its own `Data/Migrations` folder.
All contexts share one database (connection string name `starterkit`).

| Module | DbContext | Schema | Migrations folder |
|--------|-----------|--------|-------------------|
| Identity | `ApplicationIdentityDbContext` | `identity` | `src/Modules/Identity/Identity/Data/Migrations` |
| Attachment | `AttachmentDbContext` | `attachment` | `src/Modules/Attachment/Attachment/Data/Migrations` |
| Subscriptions | `SubscriptionsDbContext` | `subscriptions` | `src/Modules/Subscriptions/Subscriptions/Data/Migrations` |
| Catalog | `CatalogDbContext` | `catalog` | `src/Modules/Catalog/Catalog/Data/Migrations` |
| Tenancy | `TenancyDbContext` | `tenancy` | `src/Modules/Tenancy/Tenancy/Data/Migrations` |
| Notification | `NotificationDbContext` (in `Persistence/`) | `notification` | `src/Modules/Notification/Notification/Data/Migrations` |

Catalog and Attachment are **tenant-data** contexts: every new table, and the first `outbox_messages` table, needs
`migrationBuilder.EnableTenantRowLevelSecurity("<schema>", "<table>")` in the migration. The step-by-step checklist
is `/starterkit:add-migration` (`tools/claude/starterkit-plugin/skills/add-migration/SKILL.md`).

## Prerequisites

- PostgreSQL running (Aspire `dotnet run` in `src/Aspire/AppHost`, or `docker compose up -d postgres`)
- `dotnet tool install --global dotnet-ef`

## Add a migration

Run from the repo root. `--project` MUST point at the module project that owns the DbContext (migrations live in the
module's assembly); `--startup-project` is always `src/Api`. `--output-dir` is relative to `--project`.

```bash
dotnet ef migrations add <MigrationName> \
  --context <ModuleDbContext> \
  --project src/Modules/<Module>/<Module> \
  --startup-project src/Api \
  --output-dir Data/Migrations
```

Example:

```bash
dotnet ef migrations add AddProductSku \
  --context CatalogDbContext \
  --project src/Modules/Catalog/Catalog \
  --startup-project src/Api \
  --output-dir Data/Migrations
```

For every other `dotnet ef` command below, add the same `--project <module> --startup-project src/Api` pair.

## Apply migrations

Modules apply their migrations through `app.UseMigration<TDbContext>()` in `Use<Module>Module()`, serialized by a
Postgres advisory lock. **When** that runs is configuration:

| Setting | Dev / Aspire / tests | Production (`appsettings.Docker.json`) |
|---|---|---|
| `Database:MigrateOnStartup` | `true` (default): every instance migrates + seeds at boot | `false`: the app never migrates or seeds |
| `dotnet Api.dll --migrate-only` | optional | the compose `migrator` service runs it at deploy time |

Details, roles and the migrator connection string: [Database-Migrations](../deployment/Database-Migrations.md).
To apply manually:

```bash
dotnet ef database update --context <ModuleDbContext> --project src/Modules/<Module>/<Module> --startup-project src/Api
```

## Generate SQL without applying

```bash
dotnet ef migrations script --idempotent --context <ModuleDbContext>   --project src/Modules/<Module>/<Module> --startup-project src/Api -o ./migrations-<module>.sql   # don't commit the .sql
```

## Roll back / remove

```bash
# Roll the database back to a previous migration
dotnet ef database update <PreviousMigrationName> --context <ModuleDbContext> --project src/Modules/<Module>/<Module> --startup-project src/Api

# Remove the last migration (only if it has NOT been applied anywhere)
dotnet ef migrations remove --context <ModuleDbContext> --project src/Modules/<Module>/<Module> --startup-project src/Api
```

A migration already applied to a shared environment is never edited or deleted. Add a new migration that reverts it.

## Resetting migrations for a new project

When you start a new project from StarterKit and change the schema heavily, it is often simpler to delete a
module's `Data/Migrations` folder and create a fresh `Initial<Module>` migration for each context.
