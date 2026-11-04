# Starting a new project from StarterKit

This is the canonical checklist. With Claude Code, `/starterkit:new-project` walks you through it and verifies each
step.

## 0. Decide

| Decision | Options | Where |
|---|---|---|
| Name | `Acme` / slug `acme` | Everything below |
| Multi-tenancy | Off (default, Default tenant) / on: shared DB or dedicated DB per tenant | `MultiTenancy:Enabled`, [MultiTenancy/Overview](../MultiTenancy/Overview.md) |
| Messaging transport (prod) | `PostgreSql` (no extra infra) / `RabbitMq` | `Messaging:Transport`, [Messaging](../Architecture/Messaging.md) |
| File storage | Local / Azure Blob / S3 | [Attachment/Storage-Providers](../Attachment/Storage-Providers.md) |
| Catalog sample | Keep as reference / remove | [Modules/Catalog](../Modules/Catalog.md#removing-the-catalog-sample) |

## 1. Copy and rename

1. Copy the folder without `.git`, `bin/`, `obj/` and `node_modules/`, then run `git init`.
2. Rename `StarterKit.slnx` to `<Name>.slnx`, and update the references in `.github/workflows/*.yml`, `Makefile`,
   `README.md`, `CLAUDE.md`, `AGENTS.md` and `scripts/`.
3. Do a case-sensitive search and replace of `StarterKit` → `<Name>` and `starterkit` → `<slug>`. Exclude `bin/`,
   `obj/`, `node_modules/`, `.git/` and `Data/Migrations/`. Then check these by hand:
   - The connection-string key, which must match everywhere: `appsettings*.json`, `src/Aspire/AppHost/AppHost.cs`,
     docker-compose, each module's `AddCustomDbContext(..., "<key>")`, the integration fixtures and the design-time
     factories.
   - JWT issuer and audience, Redis instance name, container names, `/opt/<slug>`, Grafana and pgAdmin labels.
   - `.claude-plugin/marketplace.json` and `.claude/settings.json`. Keeping the plugin name `starterkit` is fine.
4. `LICENSE` is MIT with the template author's copyright. Keep it for code you reuse (MIT requires the notice), and
   add your own license and copyright for the new product.

## 2. Configure

1. Run `cp .env.example .env` and replace every `CHANGE_ME`. Never commit `.env`.
2. Run `./deployment/secrets-setup.sh` to create `secrets/*.txt`; see [Secrets](../deployment/Secrets.md).
3. Set `DOMAIN`, plus the Nginx `server_name` and admin IP allow-list in
   `deployment/nginx/conf.d/app-production.conf`.
4. Apply the decisions from step 0 in `appsettings.json` and `appsettings.Production.json`.

## 3. Remove the Catalog sample (optional)

Follow [Modules/Catalog → Removing the Catalog sample](../Modules/Catalog.md#removing-the-catalog-sample). That page
covers `Program.cs`, the csproj files, the slnx, the three architecture tests, the sample permissions, appsettings,
the schema and the Hangfire job. Also remove the Catalog rows from `README.md`, `wiki/INDEX.md` and the CLAUDE.md
messaging table.

## 4. Migrations

If you changed entities or schemas, delete the affected `Data/Migrations` folders and create one `Initial<Module>`
migration per DbContext ([Reference/Migrations](../Reference/Migrations.md)). For tenant-data contexts, re-add the
`EnableTenantRowLevelSecurity` calls, including `outbox_messages`.

## 5. GitHub

1. Create the `production` environment with required reviewers.
2. Add the repository variables (`APP_NAME`, `DEPLOY_DIR`, `PUBLIC_API_URL`, `POSTGRES_USER`, `POSTGRES_DB`,
   `BACKUP_BUCKET`) and the optional secrets (`TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`,
   `CLAUDE_CODE_OAUTH_TOKEN`). See [CI-CD](../deployment/CI-CD.md#github-setup).
3. Register the self-hosted runner with the `deploy-only` label; see
   [Self-Hosted-Runner](../deployment/Self-Hosted-Runner.md).

## 6. Verify

```bash
dotnet build <Name>.slnx
dotnet test <Name>.slnx                  # Docker running
git grep -i starterkit                   # only intentional leftovers
bash scripts/check-docs.sh               # docs links + placeholders
cd src/Aspire/AppHost && dotnet run      # then /swagger: register, log in, call an authorised endpoint
```

## 7. Build your first module

Run `/starterkit:new-module`, then `/starterkit:add-aggregate`, `/starterkit:add-feature` and
`/starterkit:write-tests`. Without Claude Code, follow the `SKILL.md` files in `tools/claude/starterkit-plugin/skills/`.
