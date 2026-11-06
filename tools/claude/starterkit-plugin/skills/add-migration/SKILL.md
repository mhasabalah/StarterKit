---
name: add-migration
description: Create, review and apply an EF Core migration for a StarterKit module DbContext with the correct --context/--project/--startup-project flags, tenant row-level security for new tables, raw-SQL tenant-first unique indexes, and the pending-model-changes check. Use for "add a migration", "update the database", "schema change", "dotnet ef", "regenerate migrations".
---

# Add an EF Core migration

Each module owns its DbContext, its own Postgres schema and its own `Data/Migrations` folder. All of them share
one database (`ConnectionStrings:starterkit`). Modules auto-migrate on startup (`UseMigration<T>()`).

## Contexts

| Module | Context | Schema | Project (`--project`) | Tenant data (RLS)? |
|---|---|---|---|---|
| Identity | `ApplicationIdentityDbContext` | `identity` | `src/Modules/Identity/Identity` | no |
| Tenancy | `TenancyDbContext` | `tenancy` | `src/Modules/Tenancy/Tenancy` | no (control plane) |
| Attachment | `AttachmentDbContext` | `attachment` | `src/Modules/Attachment/Attachment` | **yes** |
| Catalog | `CatalogDbContext` | `catalog` | `src/Modules/Catalog/Catalog` | **yes** |
| Subscriptions | `SubscriptionsDbContext` | `subscriptions` | `src/Modules/Subscriptions/Subscriptions` | no |
| Notification | `NotificationDbContext` | `notification` | `src/Modules/Notification/Notification` | no |

A context is tenant data if it implements `ITenantDataContext`.

## Steps

1. Prerequisites: `dotnet tool install --global dotnet-ef` (or `dotnet tool restore`). Postgres is only
   needed to *apply*; `migrations add` uses the design-time factory.
2. Build first: `dotnet build StarterKit.slnx`. Then run this from the **repo root**. Use a PascalCase,
   intention-revealing name (`AddStockItems`, `AddOrderNumberIndex`), never `Update1`.

   ```bash
   dotnet ef migrations add <Name> \
     --context <Context> \
     --project src/Modules/<Module>/<Module> \
     --startup-project src/Api \
     --output-dir Data/Migrations
   ```

   `--project` MUST be the module that owns the context. Otherwise you get "target project doesn't match
   migrations assembly".
3. **Review the generated `Up`/`Down` like production SQL.**
   - Check for unexpected drops or renames. EF turns a renamed property into drop + add, so hand-edit it to
     `RenameColumn` to keep the data.
   - New non-nullable columns on existing tables need a default or a backfill (`migrationBuilder.Sql("UPDATE ...")`).
   - Make sure index names match the `*IndexName` constants used by `IsUniqueViolation`.
   - Generated `Down` must reverse `Up`.
4. **Tenant-data contexts only**: add these at the end of `Up` (`using Shared.Core.MultiTenancy.Data;`):

   ```csharp
   // Database-level isolation (second layer under the EF Tenant filter).
   migrationBuilder.EnableTenantRowLevelSecurity("<schema>", "<new_table>");     // EVERY new table, children too
   // first outbox migration of the context: also "outbox_messages"
   ```

   In `Down`, call `DisableTenantRowLevelSecurity` before dropping a table you keep, or drop the table and nothing
   more. For a unique rule on an owned column (`LocalizedText`), EF cannot put `tenant_id` in the index, so create it
   in raw SQL and give it the same name as the configuration constant:

   ```csharp
   migrationBuilder.Sql("CREATE UNIQUE INDEX ux_<table>_name_en ON \"<schema>\".\"<table>\" (tenant_id, name_en);");
   ```

5. Apply it. Run the API (auto-migrates), or:

   ```bash
   dotnet ef database update --context <Context> --project src/Modules/<Module>/<Module> --startup-project src/Api
   ```

6. Generate a SQL script for review or DBA hand-off:

   ```bash
   dotnet ef migrations script --idempotent --context <Context> --project src/Modules/<Module>/<Module> --startup-project src/Api -o migrations-<module>.sql
   ```

   Don't commit the `.sql` file.

## Undo

- **Not applied anywhere yet:** run `dotnet ef migrations remove --context <Context> --project ... --startup-project src/Api`.
- **Already applied in a shared environment:** never edit or delete it. Add a new migration that reverts it.

## Guardrails

- `src/Api.E2ETests/Infrastructure/PendingModelChangesTests` fails when the model changed without a migration.
- `TenantIsolationArchitectureTests.Unique_indexes_of_tenant_owned_entities_include_tenant_id` checks the EF model:
  every unique index on a tenant entity must include `tenant_id`. The only exception is a deliberately global index,
  added with a stated reason to `GlobalUniqueIndexAllowList`. Raw-SQL indexes are not in the model, so getting them
  right is up to you.
- Commit only when the user asks, on a feature branch (CLAUDE.md > Rules > Git); otherwise leave the migration diff
  for review.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Api.E2ETests --filter "FullyQualifiedName~PendingModelChanges"   # Docker
dotnet test src/Modules/<Module>/<Module>.IntegrationTests                       # runs MigrateAsync on a real Postgres
```
