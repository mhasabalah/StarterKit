---
name: new-project
description: Bootstrap a new product from the StarterKit template - rename the solution and identifiers, configure .env and Docker secrets, decide multi-tenancy/messaging/storage, optionally remove the Catalog sample, regenerate migrations, and prepare GitHub CI/CD (environment, secrets, variables, runner). Use for "start a new project from StarterKit", "rename the template", "set up a new app from this starter", "remove the sample module".
---

# New project from StarterKit

The canonical checklist is `wiki/Getting-Started/New-Project.md`. Read it. This skill runs that checklist with the
user, in order, and verifies each step.

## 0. Ask first (one `AskUserQuestion`, up to 4 questions)

1. Project name (PascalCase, e.g. `Acme`) and lower-case slug (`acme`).
2. Multi-tenancy: **off** (single tenant, the default) or **on** (`MultiTenancy:Enabled`). With it on, ask whether
   tenants use a shared or a dedicated database.
3. Messaging transport for production: `PostgreSql` (no extra infra) or `RabbitMq`. `InMemory` is dev and tests
   only.
4. Keep the Catalog sample as reference, or remove it now?

## 1. Copy and rename

- Work in the **new** folder. Never rename in the template repo itself.
- `git mv StarterKit.slnx <Name>.slnx`, then update every reference: `.github/workflows/*.yml`, `Makefile`,
  `README.md`, `CLAUDE.md`, `AGENTS.md`, `scripts/*`.
- Case-sensitive search/replace `StarterKit` → `<Name>` and `starterkit` → `<slug>` across text files. Exclude
  `bin/`, `obj/`, `node_modules/`, `.git/` and `Data/Migrations/` (migrations get regenerated in step 4). Check
  each of these by hand:
  - The connection-string key `starterkit`. It must match in `appsettings*.json`,
    `src/Aspire/AppHost/AppHost.cs`, docker-compose, and every module's `AddCustomDbContext(..., "<key>")` and
    integration fixture.
  - JWT issuer and audience, Redis instance name, container names, `/opt/<slug>`, Grafana and pgAdmin labels.
  - `.claude-plugin/marketplace.json` and `.claude/settings.json`. The plugin can keep the name `starterkit`, or be
    renamed consistently in both files.
- Before each bulk replacement, show a `git grep -il starterkit` count, and the count again afterwards.
- `LICENSE` (MIT): keep the template's notice for reused code (MIT requires it), and ask the user which license and
  copyright holder the new product uses.

## 2. Configuration and secrets

- `cp .env.example .env`, then fill it in. Every `CHANGE_ME` must be replaced, and `.env` never gets committed.
- Create `secrets/*.txt` as listed in `secrets/README.md` (git-ignored).
- Set `DOMAIN`, and the Nginx `server_name` and admin IP allow-list in
  `deployment/nginx/conf.d/app-production.conf`.
- Apply the answers from step 0: `MultiTenancy:Enabled`, `Messaging:Transport`, and the attachment storage provider.

## 3. Optional: remove the Catalog sample

Follow `wiki/Modules/Catalog.md` → *Removing the Catalog sample*, then also:

- Remove `ProductPublishedIntegrationEvent` from the expected list in
  `src/Api.Tests/Architecture/MessagingArchitectureTests.cs`, and `CatalogDbContext` from its outbox expectation.
- Replace the expectation in `TenantIsolationArchitectureTests.Catalog_and_Attachment_are_tenant_data_contexts`
  with the remaining tenant-data contexts.
- Remove `TenantPermissions.ProductsManage` and `Permissions.CatalogModerate`, plus their grants in
  `SystemTenantRoles` and `RolePermissionMap`, unless you reuse them.
- Remove Catalog rows from the README, `wiki/INDEX.md` and the CLAUDE.md messaging table.

## 4. Migrations

If entities or schemas changed, delete the affected `Data/Migrations` folders and create one
`Initial<Module>` migration per DbContext with the `add-migration` skill. Re-add RLS calls for tenant-data
contexts, including `outbox_messages`.

## 5. GitHub

- Create a `production` environment with required reviewers.
- Add secrets: `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` (optional), plus those in `wiki/Deployment/CI-CD.md`.
- Add repository variables: `APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`,
  `BACKUP_BUCKET`.
- Register the self-hosted runner with the `deploy-only` label (`wiki/Deployment/Self-Hosted-Runner.md`).

## Verify

```bash
dotnet build <Name>.slnx
dotnet test <Name>.slnx              # Docker running
git grep -i starterkit -- . ':!*.md' # expect only intentional leftovers (plugin name, if kept)
bash scripts/check-docs.sh
```

Then start with Aspire (`cd src/Aspire/AppHost && dotnet run`), open `/swagger`, register a user, and log in.
Never commit; the user makes the first commit of the new repository.
