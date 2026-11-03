# Database Migrations

Every module owns its own `DbContext`, its own PostgreSQL schema and its own `Data/Migrations` folder.
All contexts share one database (connection string name `starterkit`).

| Module | DbContext | Schema | Migrations folder |
|--------|-----------|--------|-------------------|
| Identity | `ApplicationIdentityDbContext` | `identity` | `src/Modules/Identity/Identity/Data/Migrations` |
| Attachment | `AttachmentDbContext` | `attachment` | `src/Modules/Attachment/Attachment/Data/Migrations` |
| Subscriptions | `SubscriptionsDbContext` | `subscriptions` | `src/Modules/Subscriptions/Subscriptions/Data/Migrations` |
| Catalog | `CatalogDbContext` | `catalog` | `src/Modules/Catalog/Catalog/Data/Migrations` |
| Notification | `NotificationDbContext` | `notification` | `src/Modules/Notification/Notification/Data/Migrations` |

> Check each `DbContext.OnModelCreating` for the exact `HasDefaultSchema(...)` value.

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

Modules apply their migrations automatically at startup via `app.UseMigration<TDbContext>()` in `Use<Module>Module()`.
To apply manually:

```bash
cd src/Api
dotnet ef database update --context <ModuleDbContext>
```

## Generate SQL without applying

```bash
cd src/Api
dotnet ef migrations script --idempotent --context <ModuleDbContext> -o ./migrations-<module>.sql
```

## Roll back / remove

```bash
cd src/Api
# Roll the database back to a previous migration
dotnet ef database update <PreviousMigrationName> --context <ModuleDbContext>

# Remove the last migration (only if it has NOT been applied anywhere)
dotnet ef migrations remove --context <ModuleDbContext>
```

## Resetting migrations for a new project

When you start a new project from StarterKit and change the schema heavily, it is often simpler to delete a
module's `Data/Migrations` folder and create a fresh `Initial<Module>` migration for each context.
